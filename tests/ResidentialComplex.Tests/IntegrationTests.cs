using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Application.Services;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Domain.Enums;
using ResidentialComplex.Infrastructure.Services;
using ResidentialComplex.Persistence;
using ResidentialComplex.Persistence.Repositories;
using Xunit;

namespace ResidentialComplex.Tests;

/// <summary>
/// Base class that provides a fresh SQLite in-memory database for each test.
/// </summary>
public class TestBase : IDisposable
{
    protected readonly ApplicationDbContext Db;
    protected readonly ServiceProvider ServiceProvider;

    public TestBase()
    {
        var services = new ServiceCollection();

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite("Data Source=:memory:"));

        services.AddScoped<IApartmentRepository, ApartmentRepository>();
        services.AddScoped<IHouseRepository, HouseRepository>();
        services.AddScoped<IFinancialItemRepository, FinancialItemRepository>();
        services.AddScoped<IBillRepository, BillRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IMonthlyUsageRepository, MonthlyUsageRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<BillingService>();
        services.AddScoped<ReportService>();

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 6;
        })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        // Required for Identity
        services.AddLogging();
        services.AddDataProtection();

        ServiceProvider = services.BuildServiceProvider();

        Db = ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Db.Database.OpenConnection();
        Db.Database.EnsureCreated();
    }

    protected T GetService<T>() where T : notnull => ServiceProvider.GetRequiredService<T>();

    public void Dispose()
    {
        Db.Database.CloseConnection();
        Db.Dispose();
        ServiceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class ApartmentCrudTests : TestBase
{
    [Fact]
    public async Task Can_Create_And_Read_Apartment()
    {
        var repo = GetService<IApartmentRepository>();
        var apt = await repo.AddAsync(new Apartment { Title = "بلوک A", Description = "تست" });
        Assert.True(apt.Id > 0);

        var loaded = await repo.GetByIdAsync(apt.Id);
        Assert.NotNull(loaded);
        Assert.Equal("بلوک A", loaded.Title);
    }

    [Fact]
    public async Task Can_Update_Apartment()
    {
        var repo = GetService<IApartmentRepository>();
        var apt = await repo.AddAsync(new Apartment { Title = "بلوک B" });
        apt.Title = "بلوک B ویرایش شده";
        await repo.UpdateAsync(apt);

        var loaded = await repo.GetByIdAsync(apt.Id);
        Assert.Equal("بلوک B ویرایش شده", loaded!.Title);
    }

    [Fact]
    public async Task Can_Delete_Apartment()
    {
        var repo = GetService<IApartmentRepository>();
        var apt = await repo.AddAsync(new Apartment { Title = "بلوک حذفی" });
        await repo.DeleteAsync(apt.Id);

        var loaded = await repo.GetByIdAsync(apt.Id);
        Assert.Null(loaded);
    }
}

public class HouseCrudTests : TestBase
{
    [Fact]
    public async Task Can_Create_House_With_Apartment()
    {
        var aptRepo = GetService<IApartmentRepository>();
        var houseRepo = GetService<IHouseRepository>();

        var apt = await aptRepo.AddAsync(new Apartment { Title = "بلوک 1" });
        var house = await houseRepo.AddAsync(new House
        {
            Title = "واحد 101",
            ApartmentId = apt.Id,
            ResidentName = "علی",
            ResidentPhoneNumber = "09121234567",
            NumberOfResidents = 3,
            IsActive = true
        });

        Assert.True(house.Id > 0);
        var loaded = await houseRepo.GetByIdAsync(house.Id);
        Assert.NotNull(loaded);
        Assert.Equal("واحد 101", loaded.Title);
    }

    [Fact]
    public async Task Active_Houses_Only_Returns_Active()
    {
        var aptRepo = GetService<IApartmentRepository>();
        var houseRepo = GetService<IHouseRepository>();

        var apt = await aptRepo.AddAsync(new Apartment { Title = "بلوک" });
        await houseRepo.AddAsync(new House { Title = "فعال", ApartmentId = apt.Id, ResidentName = "ا", ResidentPhoneNumber = "0", IsActive = true });
        await houseRepo.AddAsync(new House { Title = "غیرفعال", ApartmentId = apt.Id, ResidentName = "ب", ResidentPhoneNumber = "0", IsActive = false });

        var active = await houseRepo.GetActiveHousesAsync();
        Assert.Single(active);
        Assert.Equal("فعال", active[0].Title);
    }
}

public class FinancialItemCrudTests : TestBase
{
    [Fact]
    public async Task Can_Create_Financial_Item()
    {
        var repo = GetService<IFinancialItemRepository>();
        var fi = await repo.AddAsync(new FinancialItem
        {
            Title = "شارژ ماهانه",
            PeriodType = PeriodType.Permanent,
            CalculationType = CalculationType.EqualDivision,
            IsActive = true
        });

        Assert.True(fi.Id > 0);
        var all = await repo.GetActiveAsync();
        Assert.Single(all);
    }

    [Fact]
    public async Task Installment_Financial_Item_Has_Total_And_Count()
    {
        var repo = GetService<IFinancialItemRepository>();
        var fi = await repo.AddAsync(new FinancialItem
        {
            Title = "اقساط",
            PeriodType = PeriodType.Installment,
            CalculationType = CalculationType.EqualDivision,
            TotalAmount = 12_000_000m,
            NumberOfInstallments = 6,
            IsActive = true
        });

        var loaded = await repo.GetByIdAsync(fi.Id);
        Assert.Equal(12_000_000m, loaded!.TotalAmount);
        Assert.Equal(6, loaded.NumberOfInstallments);
    }
}

public class EqualDivisionBillingTests : TestBase
{
    [Fact]
    public async Task Equal_Division_Distributes_Evenly()
    {
        var aptRepo = GetService<IApartmentRepository>();
        var houseRepo = GetService<IHouseRepository>();
        var fiRepo = GetService<IFinancialItemRepository>();
        var billingService = GetService<BillingService>();

        var apt = await aptRepo.AddAsync(new Apartment { Title = "بلوک" });
        for (int i = 1; i <= 3; i++)
            await houseRepo.AddAsync(new House { Title = $"واحد {i}", ApartmentId = apt.Id, ResidentName = "ساکن", ResidentPhoneNumber = "0", IsActive = true });

        var fi = await fiRepo.AddAsync(new FinancialItem
        {
            Title = "شارژ",
            PeriodType = PeriodType.Permanent,
            CalculationType = CalculationType.EqualDivision,
            IsActive = true
        });

        var finalAmounts = new Dictionary<int, decimal> { [fi.Id] = 300_000m };
        var bills = await billingService.GenerateBillsAsync(2025, 1, finalAmounts, "test", "test");

        Assert.Equal(3, bills.Count);
        // Total should equal final amount
        Assert.Equal(300_000m, bills.Sum(b => b.TotalAmount));
    }
}

public class GroupingBillingTests : TestBase
{
    [Fact]
    public async Task Grouping_IBT_Calculates_By_Tiers()
    {
        var aptRepo = GetService<IApartmentRepository>();
        var houseRepo = GetService<IHouseRepository>();
        var fiRepo = GetService<IFinancialItemRepository>();
        var usageRepo = GetService<IMonthlyUsageRepository>();
        var billingService = GetService<BillingService>();

        var apt = await aptRepo.AddAsync(new Apartment { Title = "بلوک" });
        var houses = new List<House>();
        for (int i = 1; i <= 3; i++)
        {
            var h = await houseRepo.AddAsync(new House { Title = $"واحد {i}", ApartmentId = apt.Id, ResidentName = "ساکن", ResidentPhoneNumber = "0", IsActive = true });
            houses.Add(h);
        }

        // IBT tiers: 0-20 units @ 1000/unit, 21-70 units @ 2000/unit, 71+ @ 4000/unit
        var fi = await fiRepo.AddAsync(new FinancialItem
        {
            Title = "گاز",
            PeriodType = PeriodType.Permanent,
            CalculationType = CalculationType.Grouping,
            IsActive = true,
            Tiers = new List<FinancialItemTier>
            {
                new() { TierOrder = 1, UpperLimit = 20, RatePerUnit = 1000m },
                new() { TierOrder = 2, UpperLimit = 70, RatePerUnit = 2000m },
                new() { TierOrder = 3, UpperLimit = null, RatePerUnit = 4000m }
            }
        });

        // Usage: house1=10 (tier1 only), house2=50 (tier1+2), house3=100 (all tiers)
        await usageRepo.AddAsync(new MonthlyUsage { HouseId = houses[0].Id, FinancialItemId = fi.Id, Year = 2025, Month = 1, UsageCount = 10 });
        await usageRepo.AddAsync(new MonthlyUsage { HouseId = houses[1].Id, FinancialItemId = fi.Id, Year = 2025, Month = 1, UsageCount = 50 });
        await usageRepo.AddAsync(new MonthlyUsage { HouseId = houses[2].Id, FinancialItemId = fi.Id, Year = 2025, Month = 1, UsageCount = 100 });

        // finalAmounts is ignored for IBT items; pass 0
        var finalAmounts = new Dictionary<int, decimal> { [fi.Id] = 0m };
        var bills = await billingService.GenerateBillsAsync(2025, 1, finalAmounts, "test", "test");

        Assert.Equal(3, bills.Count);

        var getBillForHouse = (int houseId) => bills.Single(b => b.HouseId == houseId).TotalAmount;

        // House 1: 10 * 1000 = 10,000
        Assert.Equal(10_000m, getBillForHouse(houses[0].Id));
        // House 2: 20 * 1000 + 30 * 2000 = 80,000
        Assert.Equal(80_000m, getBillForHouse(houses[1].Id));
        // House 3: 20 * 1000 + 50 * 2000 + 30 * 4000 = 240,000
        Assert.Equal(240_000m, getBillForHouse(houses[2].Id));

        // Higher usage → higher bill
        Assert.True(getBillForHouse(houses[2].Id) > getBillForHouse(houses[1].Id));
        Assert.True(getBillForHouse(houses[1].Id) > getBillForHouse(houses[0].Id));
    }
}

public class InstallmentTests : TestBase
{
    [Fact]
    public async Task Installment_Deactivates_After_All_Billed()
    {
        var aptRepo = GetService<IApartmentRepository>();
        var houseRepo = GetService<IHouseRepository>();
        var fiRepo = GetService<IFinancialItemRepository>();
        var billingService = GetService<BillingService>();

        var apt = await aptRepo.AddAsync(new Apartment { Title = "بلوک" });
        await houseRepo.AddAsync(new House { Title = "واحد 1", ApartmentId = apt.Id, ResidentName = "ساکن", ResidentPhoneNumber = "0", IsActive = true });

        var fi = await fiRepo.AddAsync(new FinancialItem
        {
            Title = "اقساط",
            PeriodType = PeriodType.Installment,
            CalculationType = CalculationType.EqualDivision,
            TotalAmount = 200_000m,
            NumberOfInstallments = 2,
            IsActive = true
        });

        // Generate and approve month 1
        await billingService.GenerateBillsAsync(2025, 1, new Dictionary<int, decimal> { [fi.Id] = 200_000m }, "test", "test");
        await billingService.ApproveBillsAsync(2025, 1, "test", "test");

        // Generate and approve month 2
        await billingService.GenerateBillsAsync(2025, 2, new Dictionary<int, decimal> { [fi.Id] = 200_000m }, "test", "test");
        await billingService.ApproveBillsAsync(2025, 2, "test", "test");

        var updatedFi = await fiRepo.GetByIdAsync(fi.Id);
        Assert.False(updatedFi!.IsActive);
    }
}

public class BillApprovalAndPaymentTests : TestBase
{
    [Fact]
    public async Task Approval_Increases_Debt_Payment_Decreases()
    {
        var aptRepo = GetService<IApartmentRepository>();
        var houseRepo = GetService<IHouseRepository>();
        var fiRepo = GetService<IFinancialItemRepository>();
        var billingService = GetService<BillingService>();

        var apt = await aptRepo.AddAsync(new Apartment { Title = "بلوک" });
        var house = await houseRepo.AddAsync(new House { Title = "واحد 1", ApartmentId = apt.Id, ResidentName = "ساکن", ResidentPhoneNumber = "0", IsActive = true, CurrentDebt = 0 });

        var fi = await fiRepo.AddAsync(new FinancialItem
        {
            Title = "شارژ",
            PeriodType = PeriodType.Permanent,
            CalculationType = CalculationType.EqualDivision,
            IsActive = true
        });

        var bills = await billingService.GenerateBillsAsync(2025, 1, new Dictionary<int, decimal> { [fi.Id] = 100_000m }, "test", "test");
        Assert.Single(bills);

        // Approve
        await billingService.ApproveBillsAsync(2025, 1, "test", "test");
        var houseAfterApproval = await houseRepo.GetByIdAsync(house.Id);
        Assert.Equal(100_000m, houseAfterApproval!.CurrentDebt);

        // Pay
        await billingService.RecordPaymentAsync(bills[0].Id, "test", "test");
        var houseAfterPayment = await houseRepo.GetByIdAsync(house.Id);
        Assert.Equal(0m, houseAfterPayment!.CurrentDebt);
    }
}

public class BillUniquenessTests : TestBase
{
    [Fact]
    public async Task Cannot_Create_Duplicate_Bill_For_Same_Month()
    {
        var aptRepo = GetService<IApartmentRepository>();
        var houseRepo = GetService<IHouseRepository>();
        var fiRepo = GetService<IFinancialItemRepository>();
        var billingService = GetService<BillingService>();

        var apt = await aptRepo.AddAsync(new Apartment { Title = "بلوک" });
        await houseRepo.AddAsync(new House { Title = "واحد 1", ApartmentId = apt.Id, ResidentName = "ساکن", ResidentPhoneNumber = "0", IsActive = true });

        var fi = await fiRepo.AddAsync(new FinancialItem { Title = "شارژ", PeriodType = PeriodType.Permanent, CalculationType = CalculationType.EqualDivision, IsActive = true });

        await billingService.GenerateBillsAsync(2025, 1, new Dictionary<int, decimal> { [fi.Id] = 100_000m }, "test", "test");
        // Second call should skip (no duplicate)
        var bills2 = await billingService.GenerateBillsAsync(2025, 1, new Dictionary<int, decimal> { [fi.Id] = 100_000m }, "test", "test");
        Assert.Empty(bills2);
    }
}

public class DebtRuleTests : TestBase
{
    [Fact]
    public async Task Debt_Can_Be_Negative()
    {
        var aptRepo = GetService<IApartmentRepository>();
        var houseRepo = GetService<IHouseRepository>();

        var apt = await aptRepo.AddAsync(new Apartment { Title = "بلوک" });
        var house = await houseRepo.AddAsync(new House { Title = "واحد 1", ApartmentId = apt.Id, ResidentName = "ساکن", ResidentPhoneNumber = "0", IsActive = true, CurrentDebt = -50_000m });

        var loaded = await houseRepo.GetByIdAsync(house.Id);
        Assert.True(loaded!.CurrentDebt < 0);
    }
}

public class AuditLogTests : TestBase
{
    [Fact]
    public async Task Billing_Creates_Audit_Logs()
    {
        var aptRepo = GetService<IApartmentRepository>();
        var houseRepo = GetService<IHouseRepository>();
        var fiRepo = GetService<IFinancialItemRepository>();
        var billingService = GetService<BillingService>();
        var auditRepo = GetService<IAuditLogRepository>();

        var apt = await aptRepo.AddAsync(new Apartment { Title = "بلوک" });
        await houseRepo.AddAsync(new House { Title = "واحد 1", ApartmentId = apt.Id, ResidentName = "ساکن", ResidentPhoneNumber = "0", IsActive = true });

        var fi = await fiRepo.AddAsync(new FinancialItem { Title = "شارژ", PeriodType = PeriodType.Permanent, CalculationType = CalculationType.EqualDivision, IsActive = true });

        await billingService.GenerateBillsAsync(2025, 1, new Dictionary<int, decimal> { [fi.Id] = 100_000m }, "testUser", "تست");

        var logs = await auditRepo.GetAllAsync();
        Assert.NotEmpty(logs);
        Assert.Contains(logs, l => l.Action == "Created" && l.EntityName == "Bill");
    }
}

public class ReportTests : TestBase
{
    [Fact]
    public async Task Report_Shows_Correct_Totals()
    {
        var aptRepo = GetService<IApartmentRepository>();
        var houseRepo = GetService<IHouseRepository>();
        var fiRepo = GetService<IFinancialItemRepository>();
        var billingService = GetService<BillingService>();
        var reportService = GetService<ReportService>();

        var apt = await aptRepo.AddAsync(new Apartment { Title = "بلوک" });
        await houseRepo.AddAsync(new House { Title = "واحد 1", ApartmentId = apt.Id, ResidentName = "ساکن", ResidentPhoneNumber = "0", IsActive = true });

        var fi = await fiRepo.AddAsync(new FinancialItem { Title = "شارژ", PeriodType = PeriodType.Permanent, CalculationType = CalculationType.EqualDivision, IsActive = true });

        var bills = await billingService.GenerateBillsAsync(2025, 1, new Dictionary<int, decimal> { [fi.Id] = 100_000m }, "test", "test");
        await billingService.ApproveBillsAsync(2025, 1, "test", "test");
        await billingService.RecordPaymentAsync(bills[0].Id, "test", "test");

        var report = await reportService.GenerateReportAsync(2025, 1, null);
        Assert.Equal(100_000m, report.TotalBilled);
        Assert.Equal(100_000m, report.TotalPaid);
        Assert.Equal(100m, report.CollectionRate);
    }
}

public class MonthlyUsageTests : TestBase
{
    [Fact]
    public async Task Can_Create_And_Update_Usage()
    {
        var aptRepo = GetService<IApartmentRepository>();
        var houseRepo = GetService<IHouseRepository>();
        var fiRepo = GetService<IFinancialItemRepository>();
        var usageRepo = GetService<IMonthlyUsageRepository>();

        var apt = await aptRepo.AddAsync(new Apartment { Title = "بلوک" });
        var house = await houseRepo.AddAsync(new House { Title = "واحد 1", ApartmentId = apt.Id, ResidentName = "ساکن", ResidentPhoneNumber = "0", IsActive = true });
        var fi = await fiRepo.AddAsync(new FinancialItem { Title = "گاز", PeriodType = PeriodType.Permanent, CalculationType = CalculationType.Grouping, IsActive = true });

        var usage = await usageRepo.AddAsync(new MonthlyUsage { HouseId = house.Id, FinancialItemId = fi.Id, Year = 2025, Month = 1, UsageCount = 50 });
        Assert.Equal(50, usage.UsageCount);

        usage.UsageCount = 75;
        await usageRepo.UpdateAsync(usage);

        var loaded = await usageRepo.GetByHouseItemMonthYearAsync(house.Id, fi.Id, 2025, 1);
        Assert.Equal(75, loaded!.UsageCount);
    }

    [Fact]
    public async Task Unique_Constraint_On_House_Item_Month_Year()
    {
        var aptRepo = GetService<IApartmentRepository>();
        var houseRepo = GetService<IHouseRepository>();
        var fiRepo = GetService<IFinancialItemRepository>();
        var usageRepo = GetService<IMonthlyUsageRepository>();

        var apt = await aptRepo.AddAsync(new Apartment { Title = "بلوک" });
        var house = await houseRepo.AddAsync(new House { Title = "واحد 1", ApartmentId = apt.Id, ResidentName = "ساکن", ResidentPhoneNumber = "0", IsActive = true });
        var fi = await fiRepo.AddAsync(new FinancialItem { Title = "گاز", PeriodType = PeriodType.Permanent, CalculationType = CalculationType.Grouping, IsActive = true });

        await usageRepo.AddAsync(new MonthlyUsage { HouseId = house.Id, FinancialItemId = fi.Id, Year = 2025, Month = 1, UsageCount = 50 });

        // Should throw on duplicate (same house, same item, same month/year)
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await usageRepo.AddAsync(new MonthlyUsage { HouseId = house.Id, FinancialItemId = fi.Id, Year = 2025, Month = 1, UsageCount = 60 }));
    }

    [Fact]
    public async Task Different_Items_Same_House_Month_Allowed()
    {
        var aptRepo = GetService<IApartmentRepository>();
        var houseRepo = GetService<IHouseRepository>();
        var fiRepo = GetService<IFinancialItemRepository>();
        var usageRepo = GetService<IMonthlyUsageRepository>();

        var apt = await aptRepo.AddAsync(new Apartment { Title = "بلوک" });
        var house = await houseRepo.AddAsync(new House { Title = "واحد 1", ApartmentId = apt.Id, ResidentName = "ساکن", ResidentPhoneNumber = "0", IsActive = true });
        var fi1 = await fiRepo.AddAsync(new FinancialItem { Title = "گاز", PeriodType = PeriodType.Permanent, CalculationType = CalculationType.Grouping, IsActive = true });
        var fi2 = await fiRepo.AddAsync(new FinancialItem { Title = "آب", PeriodType = PeriodType.Permanent, CalculationType = CalculationType.Grouping, IsActive = true });

        // Same house, same month, different financial items — should succeed
        await usageRepo.AddAsync(new MonthlyUsage { HouseId = house.Id, FinancialItemId = fi1.Id, Year = 2025, Month = 1, UsageCount = 50 });
        await usageRepo.AddAsync(new MonthlyUsage { HouseId = house.Id, FinancialItemId = fi2.Id, Year = 2025, Month = 1, UsageCount = 30 });

        var usages = await usageRepo.GetByMonthYearAsync(2025, 1);
        Assert.Equal(2, usages.Count);
    }
}

public class ValidationTests : TestBase
{
    [Fact]
    public async Task Payment_Fails_For_Draft_Bill()
    {
        var aptRepo = GetService<IApartmentRepository>();
        var houseRepo = GetService<IHouseRepository>();
        var fiRepo = GetService<IFinancialItemRepository>();
        var billingService = GetService<BillingService>();

        var apt = await aptRepo.AddAsync(new Apartment { Title = "بلوک" });
        await houseRepo.AddAsync(new House { Title = "واحد 1", ApartmentId = apt.Id, ResidentName = "ساکن", ResidentPhoneNumber = "0", IsActive = true });

        var fi = await fiRepo.AddAsync(new FinancialItem { Title = "شارژ", PeriodType = PeriodType.Permanent, CalculationType = CalculationType.EqualDivision, IsActive = true });

        var bills = await billingService.GenerateBillsAsync(2025, 1, new Dictionary<int, decimal> { [fi.Id] = 100_000m }, "test", "test");

        // Should fail - bill is Draft, not Approved
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            billingService.RecordPaymentAsync(bills[0].Id, "test", "test"));
    }

    [Fact]
    public async Task No_Active_Houses_Throws()
    {
        var fiRepo = GetService<IFinancialItemRepository>();
        var billingService = GetService<BillingService>();

        var fi = await fiRepo.AddAsync(new FinancialItem { Title = "شارژ", PeriodType = PeriodType.Permanent, CalculationType = CalculationType.EqualDivision, IsActive = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            billingService.GenerateBillsAsync(2025, 1, new Dictionary<int, decimal> { [fi.Id] = 100_000m }, "test", "test"));
    }
}

public class IdentityTests : TestBase
{
    [Fact]
    public async Task Can_Create_User_With_Role()
    {
        var userManager = GetService<UserManager<ApplicationUser>>();
        var roleManager = GetService<RoleManager<IdentityRole>>();

        await roleManager.CreateAsync(new IdentityRole("Administrator"));

        var user = new ApplicationUser { UserName = "testuser", FullName = "تست" };
        var result = await userManager.CreateAsync(user, "Test1234");
        Assert.True(result.Succeeded);

        await userManager.AddToRoleAsync(user, "Administrator");
        var roles = await userManager.GetRolesAsync(user);
        Assert.Contains("Administrator", roles);
    }

    [Fact]
    public async Task Can_Find_User_By_Username()
    {
        var userManager = GetService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser { UserName = "resident1", FullName = "ساکن یک" };
        var result = await userManager.CreateAsync(user, "Test1234");
        Assert.True(result.Succeeded);

        var found = await userManager.FindByNameAsync("resident1");
        Assert.NotNull(found);
        Assert.Equal("ساکن یک", found.FullName);
    }
}

public class OnceTypeTests : TestBase
{
    [Fact]
    public async Task Once_Type_Deactivated_After_Approval()
    {
        var aptRepo = GetService<IApartmentRepository>();
        var houseRepo = GetService<IHouseRepository>();
        var fiRepo = GetService<IFinancialItemRepository>();
        var billingService = GetService<BillingService>();

        var apt = await aptRepo.AddAsync(new Apartment { Title = "بلوک" });
        await houseRepo.AddAsync(new House { Title = "واحد 1", ApartmentId = apt.Id, ResidentName = "ساکن", ResidentPhoneNumber = "0", IsActive = true });

        var fi = await fiRepo.AddAsync(new FinancialItem { Title = "یکبار مصرف", PeriodType = PeriodType.Once, CalculationType = CalculationType.EqualDivision, IsActive = true });

        await billingService.GenerateBillsAsync(2025, 1, new Dictionary<int, decimal> { [fi.Id] = 50_000m }, "test", "test");
        await billingService.ApproveBillsAsync(2025, 1, "test", "test");

        var updated = await fiRepo.GetByIdAsync(fi.Id);
        Assert.False(updated!.IsActive);
    }
}

/// <summary>
/// Tests that verify the actual migration path (MigrateAsync) works correctly,
/// rather than using EnsureCreated which bypasses migrations entirely.
/// </summary>
public class UserCrudTests : TestBase
{
    [Fact]
    public async Task Can_Create_User_With_Worker_Role()
    {
        var userManager = GetService<UserManager<ApplicationUser>>();
        var roleManager = GetService<RoleManager<IdentityRole>>();

        await roleManager.CreateAsync(new IdentityRole("Worker"));

        var user = new ApplicationUser { UserName = "worker1", FullName = "کارگر یک" };
        var result = await userManager.CreateAsync(user, "Test1234");
        Assert.True(result.Succeeded);

        await userManager.AddToRoleAsync(user, "Worker");
        var roles = await userManager.GetRolesAsync(user);
        Assert.Contains("Worker", roles);

        var found = await userManager.FindByNameAsync("worker1");
        Assert.NotNull(found);
        Assert.Equal("کارگر یک", found.FullName);
    }

    [Fact]
    public async Task Can_Update_User_Role()
    {
        var userManager = GetService<UserManager<ApplicationUser>>();
        var roleManager = GetService<RoleManager<IdentityRole>>();

        await roleManager.CreateAsync(new IdentityRole("Worker"));
        await roleManager.CreateAsync(new IdentityRole("Resident"));

        var user = new ApplicationUser { UserName = "changeme", FullName = "تست" };
        await userManager.CreateAsync(user, "Test1234");
        await userManager.AddToRoleAsync(user, "Worker");

        // Change role to Resident
        var currentRoles = await userManager.GetRolesAsync(user);
        await userManager.RemoveFromRolesAsync(user, currentRoles);
        await userManager.AddToRoleAsync(user, "Resident");

        var updatedRoles = await userManager.GetRolesAsync(user);
        Assert.DoesNotContain("Worker", updatedRoles);
        Assert.Contains("Resident", updatedRoles);
    }

    [Fact]
    public async Task Can_Delete_User()
    {
        var userManager = GetService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser { UserName = "deleteme", FullName = "حذفی" };
        await userManager.CreateAsync(user, "Test1234");

        var found = await userManager.FindByNameAsync("deleteme");
        Assert.NotNull(found);

        await userManager.DeleteAsync(found);

        var deleted = await userManager.FindByNameAsync("deleteme");
        Assert.Null(deleted);
    }

    [Fact]
    public async Task Create_User_Without_Password_Fails()
    {
        var userManager = GetService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser { UserName = "nopass", FullName = "بدون رمز" };
        var result = await userManager.CreateAsync(user, "");
        Assert.False(result.Succeeded);
    }
}

public class MigrationTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ServiceProvider _serviceProvider;

    public MigrationTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite("Data Source=:memory:"));
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 6;
        })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
        services.AddLogging();
        services.AddDataProtection();

        _serviceProvider = services.BuildServiceProvider();
        _db = _serviceProvider.GetRequiredService<ApplicationDbContext>();
        _db.Database.OpenConnection();
    }

    [Fact]
    public async Task MigrateAsync_Creates_All_Tables()
    {
        await _db.Database.MigrateAsync();

        // Verify Identity tables exist by querying them
        var connection = _db.Database.GetDbConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM AspNetRoles";
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.True(count >= 0);

        // Verify domain tables exist by inserting data
        _db.Apartments.Add(new Apartment { Title = "Test" });
        await _db.SaveChangesAsync();
        Assert.Equal(1, await _db.Apartments.CountAsync());
    }

    [Fact]
    public async Task MigrateAsync_Supports_Seeding_Roles_And_Admin()
    {
        await _db.Database.MigrateAsync();

        var userManager = _serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = _serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        // Seed roles
        foreach (var role in new[] { "Administrator", "Worker", "Resident" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        // Seed admin by username (not email)
        var admin = new ApplicationUser { UserName = "admin", FullName = "Admin" };
        var result = await userManager.CreateAsync(admin, "Admin1234");
        Assert.True(result.Succeeded);
        await userManager.AddToRoleAsync(admin, "Administrator");

        var loaded = await userManager.FindByNameAsync("admin");
        Assert.NotNull(loaded);
        Assert.True(await userManager.IsInRoleAsync(loaded, "Administrator"));
    }

    [Fact]
    public async Task MigrateAsync_Creates_Domain_Tables_With_Correct_Schema()
    {
        await _db.Database.MigrateAsync();

        // Create full object graph to verify FK constraints work
        var apt = new Apartment { Title = "بلوک A" };
        _db.Apartments.Add(apt);
        await _db.SaveChangesAsync();

        var house = new House
        {
            Title = "واحد 1",
            ApartmentId = apt.Id,
            ResidentName = "تست",
            ResidentPhoneNumber = "09120000000",
            NumberOfResidents = 2,
            IsActive = true
        };
        _db.Houses.Add(house);
        await _db.SaveChangesAsync();

        var bill = new Bill
        {
            HouseId = house.Id,
            Year = 2025,
            Month = 1,
            TotalAmount = 100_000m,
            Description = "تست",
            Status = BillStatus.Draft,
            CreatedDate = DateTime.UtcNow
        };
        _db.Bills.Add(bill);
        await _db.SaveChangesAsync();

        Assert.True(bill.Id > 0);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
