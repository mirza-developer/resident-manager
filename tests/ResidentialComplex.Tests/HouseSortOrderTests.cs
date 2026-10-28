using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Domain.Enums;
using ResidentialComplex.Persistence;
using Xunit;

namespace ResidentialComplex.Tests;

/// <summary>The rule that gives a house its initial manual order from its title.</summary>
public class HouseSortOrderTitleTests
{
    [Theory]
    [InlineData("واحد 1", 1)]
    [InlineData("واحد 12", 12)]
    [InlineData("واحد 007", 7)]
    [InlineData("  واحد   5  ", 5)]
    [InlineData("واحد ۱۲", 12)]       // Persian digits
    [InlineData("واحد ٣٤", 34)]       // Arabic-Indic digits
    [InlineData("واحد 123456789", 123456789)]
    [InlineData("واحد 0", 0)]
    public void DefaultSortOrderFromTitle_UnitNumber_ReturnsTheNumber(string title, int expected)
    {
        Assert.Equal(expected, House.DefaultSortOrderFromTitle(title));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("واحد")]
    [InlineData("واحد x")]
    [InlineData("واحد 1a")]
    [InlineData("واحد 12 ب")]
    [InlineData("واحد -3")]
    [InlineData("واحد 1.5")]
    [InlineData("واحد۵")]               // no separator
    [InlineData("پنت هاوس")]
    [InlineData("Unit 5")]
    [InlineData("بلوک A واحد 5")]        // must be the whole title
    [InlineData("واحد 1234567890")]      // too big for an int
    public void DefaultSortOrderFromTitle_OtherTitles_ReturnZero(string? title)
    {
        Assert.Equal(0, House.DefaultSortOrderFromTitle(title));
    }

    [Fact]
    public void NewHouse_StartsWithSortOrderZero()
    {
        Assert.Equal(0, new House().SortOrder);
    }
}

/// <summary>Every list of houses comes back in ascending SortOrder (ties by Id); the order is stored and editable.</summary>
public class HouseSortOrderPersistenceTests : TestBase
{
    private async Task<Apartment> AddApartmentAsync(string title = "بلوک") =>
        await GetService<IApartmentRepository>().AddAsync(new Apartment { Title = title });

    private async Task<House> AddHouseAsync(Apartment apartment, string title, int sortOrder = 0, bool active = true) =>
        await GetService<IHouseRepository>().AddAsync(new House
        {
            Title = title,
            ApartmentId = apartment.Id,
            ResidentName = "ساکن",
            ResidentPhoneNumber = "09120000000",
            IsActive = active,
            SortOrder = sortOrder
        });

    [Fact]
    public async Task AddAsync_DerivesTheOrderFromAUnitTitle()
    {
        var apt = await AddApartmentAsync();

        var h12 = await AddHouseAsync(apt, "واحد 12");
        var hFa = await AddHouseAsync(apt, "واحد ۷");
        var other = await AddHouseAsync(apt, "پنت هاوس");

        Assert.Equal(12, h12.SortOrder);
        Assert.Equal(7, hFa.SortOrder);
        Assert.Equal(0, other.SortOrder);

        Db.ChangeTracker.Clear();
        Assert.Equal(12, (await GetService<IHouseRepository>().GetByIdAsync(h12.Id))!.SortOrder);
    }

    [Fact]
    public async Task AddAsync_KeepsAnExplicitOrder()
    {
        var apt = await AddApartmentAsync();

        var house = await AddHouseAsync(apt, "واحد 12", sortOrder: 5);

        Assert.Equal(5, house.SortOrder);
    }

    [Fact]
    public async Task GetAllAsync_IsSortedByOrder_NotByInsertionOrOrTitleText()
    {
        var apt = await AddApartmentAsync();
        await AddHouseAsync(apt, "واحد 10");
        await AddHouseAsync(apt, "واحد 2");
        await AddHouseAsync(apt, "واحد 1");
        await AddHouseAsync(apt, "پنت هاوس"); // 0 → first

        var titles = (await GetService<IHouseRepository>().GetAllAsync()).Select(h => h.Title).ToList();

        Assert.Equal(new[] { "پنت هاوس", "واحد 1", "واحد 2", "واحد 10" }, titles);
    }

    [Fact]
    public async Task EqualOrders_FallBackToId()
    {
        var apt = await AddApartmentAsync();
        var a = await AddHouseAsync(apt, "الف", 10);
        var b = await AddHouseAsync(apt, "ب", 10);
        var c = await AddHouseAsync(apt, "ج", 10);

        var ids = (await GetService<IHouseRepository>().GetAllAsync()).Select(h => h.Id).ToList();

        Assert.Equal(new[] { a.Id, b.Id, c.Id }, ids);
    }

    [Fact]
    public async Task NegativeOrders_SortBeforeZero()
    {
        var apt = await AddApartmentAsync();
        await AddHouseAsync(apt, "صفر", 1);
        await AddHouseAsync(apt, "منفی", -5);

        var titles = (await GetService<IHouseRepository>().GetAllAsync()).Select(h => h.Title).ToList();

        Assert.Equal(new[] { "منفی", "صفر" }, titles);
    }

    [Fact]
    public async Task GetActiveHousesAsync_IsSortedAndSkipsInactiveHouses()
    {
        var apt = await AddApartmentAsync();
        await AddHouseAsync(apt, "سوم", 30);
        await AddHouseAsync(apt, "غیرفعال", 5, active: false);
        await AddHouseAsync(apt, "اول", 10);
        await AddHouseAsync(apt, "دوم", 20);

        var titles = (await GetService<IHouseRepository>().GetActiveHousesAsync()).Select(h => h.Title).ToList();

        Assert.Equal(new[] { "اول", "دوم", "سوم" }, titles);
    }

    [Fact]
    public async Task GetByApartmentIdAsync_IsSortedAndOnlyReturnsThatApartment()
    {
        var apt1 = await AddApartmentAsync("بلوک ۱");
        var apt2 = await AddApartmentAsync("بلوک ۲");
        await AddHouseAsync(apt1, "ب", 20);
        await AddHouseAsync(apt2, "ناپیدا", 1);
        await AddHouseAsync(apt1, "الف", 10);

        var titles = (await GetService<IHouseRepository>().GetByApartmentIdAsync(apt1.Id)).Select(h => h.Title).ToList();

        Assert.Equal(new[] { "الف", "ب" }, titles);
    }

    [Fact]
    public async Task ApartmentRepository_ReturnsItsHousesInOrder()
    {
        var apt = await AddApartmentAsync();
        await AddHouseAsync(apt, "ب", 20);
        await AddHouseAsync(apt, "الف", 10);
        Db.ChangeTracker.Clear();

        var all = await GetService<IApartmentRepository>().GetAllAsync();
        var byId = await GetService<IApartmentRepository>().GetByIdAsync(apt.Id);

        Assert.Equal(new[] { "الف", "ب" }, all.Single().Houses.Select(h => h.Title));
        Assert.Equal(new[] { "الف", "ب" }, byId!.Houses.Select(h => h.Title));
        Assert.Equal(2, all.Single().Houses.Count);
    }

    [Fact]
    public async Task UpdateAsync_SavesTheNewOrder_AndTheListIsResorted()
    {
        var apt = await AddApartmentAsync();
        var first = await AddHouseAsync(apt, "اول", 10);
        await AddHouseAsync(apt, "دوم", 20);
        var repo = GetService<IHouseRepository>();

        first.SortOrder = 99;
        await repo.UpdateAsync(first);
        Db.ChangeTracker.Clear();

        Assert.Equal(new[] { "دوم", "اول" }, (await repo.GetAllAsync()).Select(h => h.Title));
        Assert.Equal(99, (await repo.GetByIdAsync(first.Id))!.SortOrder);
    }

    [Fact]
    public async Task UpdateAsync_OfAnotherField_KeepsTheOrder()
    {
        var apt = await AddApartmentAsync();
        var house = await AddHouseAsync(apt, "واحد 3");
        var repo = GetService<IHouseRepository>();
        Db.ChangeTracker.Clear();

        // The edit form works on a copy of the house; this is what it saves.
        var copy = (await repo.GetByIdAsync(house.Id))!;
        var edited = new House
        {
            Id = copy.Id, Title = copy.Title, ApartmentId = copy.ApartmentId, ResidentName = "نام جدید",
            ResidentPhoneNumber = copy.ResidentPhoneNumber, NumberOfResidents = copy.NumberOfResidents,
            IsActive = copy.IsActive, SortOrder = copy.SortOrder, CurrentDebt = copy.CurrentDebt, RowVersion = copy.RowVersion
        };
        Db.ChangeTracker.Clear();
        await repo.UpdateAsync(edited);
        Db.ChangeTracker.Clear();

        var reloaded = (await repo.GetByIdAsync(house.Id))!;
        Assert.Equal("نام جدید", reloaded.ResidentName);
        Assert.Equal(3, reloaded.SortOrder);
    }

    [Fact]
    public async Task Bills_OfAPeriod_AreListedInHouseOrder()
    {
        var apt = await AddApartmentAsync();
        var h3 = await AddHouseAsync(apt, "سه", 30);
        var h1 = await AddHouseAsync(apt, "یک", 10);
        var h2 = await AddHouseAsync(apt, "دو", 20);
        var bills = GetService<IBillRepository>();
        foreach (var h in new[] { h3, h1, h2 }) // inserted in a different order than the houses' order
        {
            await bills.AddAsync(new Bill { HouseId = h.Id, Year = 2025, Month = 1, TotalAmount = 1000m, Description = "تست", Status = BillStatus.Draft, CreatedDate = DateTime.Now });
        }

        Db.ChangeTracker.Clear();
        var period = await bills.GetByMonthYearAsync(2025, 1);
        var all = await bills.GetAllAsync();
        var report = await bills.GetForReportAsync(2025, 1, null);

        var expected = new[] { h1.Id, h2.Id, h3.Id };
        Assert.Equal(expected, period.Select(b => b.HouseId));
        Assert.Equal(expected, all.Select(b => b.HouseId));
        Assert.Equal(expected, report.Select(b => b.HouseId));
    }

    [Fact]
    public async Task MonthlyUsages_AreListedInHouseOrder()
    {
        var apt = await AddApartmentAsync();
        var h2 = await AddHouseAsync(apt, "دو", 20);
        var h1 = await AddHouseAsync(apt, "یک", 10);
        var fi = await GetService<IFinancialItemRepository>().AddAsync(new FinancialItem
        {
            Title = "گاز",
            PeriodType = PeriodType.Permanent,
            CalculationType = CalculationType.ParentChildTiering,
            IsActive = true,
            Tiers = new List<FinancialItemTier> { new() { TierOrder = 1, UpperLimit = null, RatePerUnit = 1000m } }
        });
        var usages = GetService<IMonthlyUsageRepository>();
        await usages.AddAsync(new MonthlyUsage { HouseId = h2.Id, FinancialItemId = fi.Id, Year = 2025, Month = 1, UsageCount = 5 });
        await usages.AddAsync(new MonthlyUsage { HouseId = h1.Id, FinancialItemId = fi.Id, Year = 2025, Month = 1, UsageCount = 7 });

        Db.ChangeTracker.Clear();
        var byMonth = await usages.GetByMonthYearAsync(2025, 1);
        var byItem = await usages.GetByFinancialItemMonthYearAsync(fi.Id, 2025, 1);

        Assert.Equal(new[] { h1.Id, h2.Id }, byMonth.Select(u => u.HouseId));
        Assert.Equal(new[] { h1.Id, h2.Id }, byItem.Select(u => u.HouseId));
    }

    [Fact]
    public async Task DeletingAHouse_StillWorks()
    {
        var apt = await AddApartmentAsync();
        var house = await AddHouseAsync(apt, "واحد 1");

        await GetService<IHouseRepository>().DeleteAsync(house.Id);

        Assert.Empty(await GetService<IHouseRepository>().GetAllAsync());
    }
}

/// <summary>Runs the real migrations on SQLite: the column is added and existing houses get their initial order.</summary>
public class HouseSortOrderMigrationTests : IDisposable
{
    private const string PreviousMigration = "20250101000011_AddTierMonthlyPrice";

    private readonly ApplicationDbContext _db;
    private readonly ServiceProvider _serviceProvider;

    public HouseSortOrderMigrationTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite("Data Source=:memory:"));
        services.AddIdentity<ApplicationUser, Microsoft.AspNetCore.Identity.IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
        services.AddLogging();
        services.AddDataProtection();

        _serviceProvider = services.BuildServiceProvider();
        _db = _serviceProvider.GetRequiredService<ApplicationDbContext>();
        _db.Database.OpenConnection();
    }

    private async Task ExecAsync(string sql)
    {
        using var cmd = _db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> HouseColumnsAsync()
    {
        var names = new List<string>();
        using var cmd = _db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT name FROM pragma_table_info('Houses')";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return names;
    }

    private async Task<Dictionary<string, int>> SortOrdersByTitleAsync()
    {
        var result = new Dictionary<string, int>();
        using var cmd = _db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT Title, SortOrder FROM Houses";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result[reader.GetString(0)] = reader.GetInt32(1);
        return result;
    }

    [Fact]
    public void NewMigration_IsLast_AndKeepsTheOlderOnes()
    {
        var all = _db.Database.GetMigrations().ToList();

        Assert.Equal("20250101000012_AddHouseSortOrder", all.Last());
        Assert.Equal(PreviousMigration, all[^2]);
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public async Task Migrate_AddsTheSortOrderColumn()
    {
        await _db.Database.MigrateAsync();

        Assert.Contains("SortOrder", await HouseColumnsAsync());
    }

    [Fact]
    public async Task Migrate_BackfillsExistingHousesFromTheirTitles()
    {
        await _db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        Assert.DoesNotContain("SortOrder", await HouseColumnsAsync());

        await ExecAsync("INSERT INTO Apartments (Title, RowVersion) VALUES ('بلوک', 0)");
        var titles = new[] { "واحد 1", "واحد 12", "واحد ۱۲", "  واحد   7  ", "واحد 007", "واحد", "واحد x", "واحد 1a", "پنت هاوس", "Unit 5", "واحد 1234567890", "واحد -3" };
        foreach (var title in titles)
        {
            // Titles are unique per row here; a numeric suffix keeps duplicates (۱۲ vs 12) distinguishable by Id below.
            await ExecAsync($"INSERT INTO Houses (Title, ResidentName, ResidentPhoneNumber, NumberOfResidents, CurrentDebt, IsActive, RowVersion, ApartmentId) VALUES ('{title}', 'ساکن', '0912', 1, 0, 1, 0, 1)");
        }

        await _db.Database.MigrateAsync();

        var orders = await SortOrdersByTitleAsync();
        Assert.Equal(1, orders["واحد 1"]);
        Assert.Equal(12, orders["واحد 12"]);
        Assert.Equal(12, orders["واحد ۱۲"]);
        Assert.Equal(7, orders["  واحد   7  "]);
        Assert.Equal(7, orders["واحد 007"]);
        Assert.Equal(0, orders["واحد"]);
        Assert.Equal(0, orders["واحد x"]);
        Assert.Equal(0, orders["واحد 1a"]);
        Assert.Equal(0, orders["پنت هاوس"]);
        Assert.Equal(0, orders["Unit 5"]);
        Assert.Equal(0, orders["واحد 1234567890"]);
        Assert.Equal(0, orders["واحد -3"]);

        // The SQL backfill and the C# rule used for new houses must agree.
        foreach (var (title, order) in orders)
            Assert.Equal(House.DefaultSortOrderFromTitle(title), order);
    }

    [Fact]
    public async Task MigratedSchema_AcceptsHousesWrittenThroughTheModel()
    {
        await _db.Database.MigrateAsync();

        _db.Apartments.Add(new Apartment { Title = "برج" });
        await _db.SaveChangesAsync();
        _db.Houses.Add(new House { Title = "واحد 4", ApartmentId = 1, ResidentName = "ساکن", ResidentPhoneNumber = "0912", IsActive = true, SortOrder = 4 });
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        Assert.Equal(4, (await _db.Houses.SingleAsync()).SortOrder);
    }

    [Fact]
    public async Task Down_RemovesTheColumn_AndKeepsTheHouses()
    {
        await _db.Database.MigrateAsync();
        await ExecAsync("INSERT INTO Apartments (Title, RowVersion) VALUES ('بلوک', 0)");
        await ExecAsync("INSERT INTO Houses (Title, ResidentName, ResidentPhoneNumber, NumberOfResidents, CurrentDebt, IsActive, RowVersion, ApartmentId, SortOrder) VALUES ('واحد 3', 'ساکن', '0912', 1, 0, 1, 0, 1, 3)");

        await _db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        Assert.DoesNotContain("SortOrder", await HouseColumnsAsync());
        using var cmd = _db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Houses";
        Assert.Equal(1L, Convert.ToInt64(await cmd.ExecuteScalarAsync()));
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
