using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResidentialComplex.Application.DTOs;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Application.Services;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Domain.Enums;
using ResidentialComplex.Persistence;
using Xunit;

namespace ResidentialComplex.Tests;

/// <summary>
/// Online payment against the real EF repositories on SQLite: the atomic conditional updates,
/// the transaction, the unique indexes and the complete flow with a fake Zibal.
/// </summary>
public class PaymentPersistenceTests : TestBase
{
    private const string OwnerId = "resident-1";

    private PaymentService Service => GetService<PaymentService>();
    private FakePaymentGateway Gateway => GetService<FakePaymentGateway>();
    private IPaymentAttemptRepository Attempts => GetService<IPaymentAttemptRepository>();

    private async Task<(House House, Bill Bill)> ArrangeAsync(decimal amount = 500_000, string ownerId = OwnerId, string title = "واحد 1", int month = 1)
    {
        var apartment = await GetService<IApartmentRepository>().AddAsync(new Apartment { Title = $"بلوک {title}" });
        var house = await GetService<IHouseRepository>().AddAsync(new House
        {
            Title = title,
            ApartmentId = apartment.Id,
            ResidentName = "ساکن",
            ResidentPhoneNumber = "09123456789",
            NumberOfResidents = 2,
            IsActive = true,
            ApplicationUserId = ownerId,
            CurrentDebt = amount
        });
        var bill = await GetService<IBillRepository>().AddAsync(new Bill
        {
            HouseId = house.Id,
            Year = 1404,
            Month = month,
            TotalAmount = amount,
            Status = BillStatus.Approved,
            CreatedDate = DateTime.Now,
            ApprovedDate = DateTime.Now
        });
        return (house, bill);
    }

    private async Task<Bill> ReloadBillAsync(int id)
    {
        Db.ChangeTracker.Clear();
        return await Db.Bills.AsNoTracking().SingleAsync(b => b.Id == id);
    }

    private async Task<House> ReloadHouseAsync(int id)
    {
        Db.ChangeTracker.Clear();
        return await Db.Houses.AsNoTracking().SingleAsync(h => h.Id == id);
    }

    private async Task<(Guid PublicId, long TrackId, string OrderId)> StartAsync(Bill bill, string userId = OwnerId)
    {
        var start = await Service.StartPaymentAsync(bill.Id, userId, userId, "https://site.example/payment/callback");
        Assert.True(start.Success, start.ErrorMessage);
        var attempt = (await Attempts.GetByPublicIdAsync(start.AttemptPublicId!.Value))!;
        return (attempt.PublicId, attempt.TrackId!.Value, attempt.OrderId);
    }

    private Task<PaymentProcessingResult> CallbackAsync(long track, string orderId, string success = "1") =>
        Service.HandleCallbackAsync(new PaymentCallback(track, success, "2", orderId, $"?trackId={track}&success={success}"));

    // ------------------------------------------------------------------ full flow

    [Fact]
    public async Task SuccessfulPayment_IsStoredCompletely()
    {
        var (house, bill) = await ArrangeAsync();
        var (publicId, track, orderId) = await StartAsync(bill);
        Gateway.Pay(track);

        var result = await CallbackAsync(track, orderId);

        Assert.Equal(PaymentProcessingOutcome.Succeeded, result.Outcome);

        var paidBill = await ReloadBillAsync(bill.Id);
        Assert.Equal(BillStatus.Paid, paidBill.Status);
        Assert.NotNull(paidBill.PaidDate);

        var payments = await Db.Payments.AsNoTracking().Where(p => p.BillId == bill.Id).ToListAsync();
        Assert.Single(payments);
        Assert.Equal(500_000m, payments[0].Amount);

        Assert.Equal(0m, (await ReloadHouseAsync(house.Id)).CurrentDebt);

        var attempt = (await Attempts.GetByPublicIdAsync(publicId))!;
        Assert.Equal(PaymentAttemptStatus.Succeeded, attempt.Status);
        Assert.Equal(payments[0].Id, attempt.PaymentId);
        Assert.Equal(track, attempt.TrackId);
        Assert.Equal(987654, attempt.RefNumber);
        Assert.Equal("6037****1234", attempt.CardNumber);
        Assert.Equal(500_000m, attempt.PaidAmount);
        Assert.NotNull(attempt.VerifiedAt);
        Assert.Equal(house.Id, attempt.Bill.HouseId);

        var types = attempt.Events.Select(e => e.EventType).ToList();
        Assert.Contains(PaymentEventType.Created, types);
        Assert.Contains(PaymentEventType.GatewayRequest, types);
        Assert.Contains(PaymentEventType.Callback, types);
        Assert.Contains(PaymentEventType.Inquiry, types);
        Assert.Contains(PaymentEventType.Verify, types);
        Assert.Contains(PaymentEventType.Settled, types);
        Assert.Equal(attempt.Events.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).Select(e => e.Id), attempt.Events.Select(e => e.Id));
    }

    [Fact]
    public async Task ReplayedCallback_DoesNotCreditTwice()
    {
        var (house, bill) = await ArrangeAsync();
        var (_, track, orderId) = await StartAsync(bill);
        Gateway.Pay(track);
        await CallbackAsync(track, orderId);

        var replay = await CallbackAsync(track, orderId);

        Assert.Equal(PaymentProcessingOutcome.AlreadyFinal, replay.Outcome);
        Assert.Equal(1, Gateway.VerifyCalls);
        Assert.Equal(1, await Db.Payments.CountAsync(p => p.BillId == bill.Id));
        Assert.Equal(0m, (await ReloadHouseAsync(house.Id)).CurrentDebt);
    }

    [Fact]
    public async Task ForgedSuccessCallback_ForAnUnpaidSession_KeepsTheBillApproved()
    {
        var (_, bill) = await ArrangeAsync();
        var (publicId, track, orderId) = await StartAsync(bill);

        var result = await CallbackAsync(track, orderId, "1");

        Assert.Equal(PaymentProcessingOutcome.Pending, result.Outcome);
        Assert.Equal(BillStatus.Approved, (await ReloadBillAsync(bill.Id)).Status);
        Assert.Equal(0, await Db.Payments.CountAsync());
        Assert.Equal(PaymentAttemptStatus.CallbackReceived, (await Attempts.GetByPublicIdAsync(publicId))!.Status);
    }

    [Fact]
    public async Task AmountMismatch_IsFlaggedForReview_AndDoesNotPayTheBill()
    {
        var (house, bill) = await ArrangeAsync();
        var (publicId, track, orderId) = await StartAsync(bill);
        Gateway.Pay(track);
        Gateway.AmountOverride = 10_000;

        var result = await CallbackAsync(track, orderId);

        Assert.Equal(PaymentProcessingOutcome.NeedsReview, result.Outcome);
        Assert.Equal(BillStatus.Approved, (await ReloadBillAsync(bill.Id)).Status);
        Assert.Equal(500_000m, (await ReloadHouseAsync(house.Id)).CurrentDebt);
        var attempt = (await Attempts.GetByPublicIdAsync(publicId))!;
        Assert.Equal(PaymentAttemptStatus.NeedsReview, attempt.Status);
        Assert.Equal(10_000m, attempt.PaidAmount);
    }

    [Fact]
    public async Task OnlyTheHouseOwner_CanStartAPayment()
    {
        var (_, bill) = await ArrangeAsync();

        var result = await Service.StartPaymentAsync(bill.Id, "someone-else", "x", "https://site.example/payment/callback");

        Assert.False(result.Success);
        Assert.Equal(0, await Db.PaymentAttempts.CountAsync());
    }

    [Fact]
    public async Task ResultVisibility_OwnerAndAdministratorOnly()
    {
        var (_, bill) = await ArrangeAsync();
        var (publicId, _, _) = await StartAsync(bill);

        Assert.NotNull(await Service.GetForUserAsync(publicId, OwnerId, false));
        Assert.Null(await Service.GetForUserAsync(publicId, "another-resident", false));
        Assert.NotNull(await Service.GetForUserAsync(publicId, "admin-1", true));
    }

    [Fact]
    public async Task GatewayRequestFailure_IsStoredAsRequestFailed()
    {
        var (_, bill) = await ArrangeAsync();
        Gateway.RequestResult = 115;

        var result = await Service.StartPaymentAsync(bill.Id, OwnerId, OwnerId, "https://site.example/payment/callback");

        Assert.False(result.Success);
        var stored = await Db.PaymentAttempts.AsNoTracking().SingleAsync();
        Assert.Equal(PaymentAttemptStatus.RequestFailed, stored.Status);
        Assert.Equal(115, stored.RequestResultCode);
        Assert.Null(stored.TrackId);
    }

    // ------------------------------------------------------------------ atomic repository operations

    private async Task<PaymentAttempt> AddAttemptAsync(int billId, PaymentAttemptStatus status, long? trackId = null, string? orderId = null, DateTime? updatedAt = null)
    {
        var now = DateTime.Now;
        return await Attempts.AddAsync(new PaymentAttempt
        {
            BillId = billId,
            Amount = 500_000m,
            OrderId = orderId ?? Guid.NewGuid().ToString("N"),
            TrackId = trackId,
            Status = status,
            CreatedAt = now,
            UpdatedAt = updatedAt ?? now
        });
    }

    [Fact]
    public async Task TryClaim_AllowsOnlyOneWinner()
    {
        var (_, bill) = await ArrangeAsync();
        var attempt = await AddAttemptAsync(bill.Id, PaymentAttemptStatus.Requested, 1001);
        var now = DateTime.Now;

        Assert.True(await Attempts.TryClaimAsync(attempt.Id, now, now.AddMinutes(-2)));
        Assert.False(await Attempts.TryClaimAsync(attempt.Id, now, now.AddMinutes(-2)));   // lease is fresh
        Assert.Equal(PaymentAttemptStatus.Verifying, (await Attempts.GetByIdAsync(attempt.Id))!.Status);

        Assert.True(await Attempts.TryReleaseAsync(attempt.Id, PaymentAttemptStatus.CallbackReceived, "retry later", now));
        Assert.False(await Attempts.TryReleaseAsync(attempt.Id, PaymentAttemptStatus.CallbackReceived, null, now));   // no longer Verifying
        var released = (await Attempts.GetByIdAsync(attempt.Id))!;
        Assert.Equal(PaymentAttemptStatus.CallbackReceived, released.Status);
        Assert.Equal("retry later", released.Note);
    }

    [Fact]
    public async Task TryClaim_ReclaimsAStaleLease_ButNeverAFinalAttempt()
    {
        var (_, bill) = await ArrangeAsync();
        var now = DateTime.Now;
        var stale = await AddAttemptAsync(bill.Id, PaymentAttemptStatus.Verifying, 1002, updatedAt: now.AddMinutes(-10));
        var done = await AddAttemptAsync(bill.Id, PaymentAttemptStatus.Succeeded, 1003);

        Assert.True(await Attempts.TryClaimAsync(stale.Id, now, now.AddMinutes(-2)));
        Assert.False(await Attempts.TryClaimAsync(done.Id, now, now.AddMinutes(-2)));
        Assert.Equal(PaymentAttemptStatus.Succeeded, (await Attempts.GetByIdAsync(done.Id))!.Status);
    }

    [Fact]
    public async Task TryMarkCallbackReceived_OnlyMovesRequestedAttempts()
    {
        var (_, bill) = await ArrangeAsync();
        var requested = await AddAttemptAsync(bill.Id, PaymentAttemptStatus.Requested, 1004);
        var finished = await AddAttemptAsync(bill.Id, PaymentAttemptStatus.Failed, 1005);

        Assert.True(await Attempts.TryMarkCallbackReceivedAsync(requested.Id, true, 2, DateTime.Now));
        Assert.False(await Attempts.TryMarkCallbackReceivedAsync(finished.Id, true, 2, DateTime.Now));

        var moved = (await Attempts.GetByIdAsync(requested.Id))!;
        Assert.Equal(PaymentAttemptStatus.CallbackReceived, moved.Status);
        Assert.True(moved.CallbackSuccess);
        Assert.Equal(2, moved.CallbackStatus);
        Assert.NotNull(moved.CallbackReceivedAt);
        Assert.Equal(PaymentAttemptStatus.Failed, (await Attempts.GetByIdAsync(finished.Id))!.Status);
    }

    [Fact]
    public async Task TryMarkPaid_WorksOnlyOnceAndOnlyForApprovedBills()
    {
        var (_, bill) = await ArrangeAsync();
        var billRepo = GetService<IBillRepository>();
        var paidAt = new DateTime(2025, 3, 20, 10, 0, 0);

        Assert.True(await billRepo.TryMarkPaidAsync(bill.Id, paidAt));
        Assert.False(await billRepo.TryMarkPaidAsync(bill.Id, paidAt));

        var stored = await ReloadBillAsync(bill.Id);
        Assert.Equal(BillStatus.Paid, stored.Status);
        Assert.Equal(paidAt, stored.PaidDate);

        var (_, draft) = await ArrangeAsync(title: "واحد 2", ownerId: "resident-2");
        draft.Status = BillStatus.Draft;
        await billRepo.UpdateAsync(draft);
        Assert.False(await billRepo.TryMarkPaidAsync(draft.Id, paidAt));
        Assert.Equal(BillStatus.Draft, (await ReloadBillAsync(draft.Id)).Status);
    }

    [Fact]
    public async Task Transaction_RollsBackConditionalUpdates_WhenTheWorkFails()
    {
        var (_, bill) = await ArrangeAsync();
        var runner = GetService<ITransactionRunner>();
        var billRepo = GetService<IBillRepository>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ExecuteAsync<bool>(async () =>
        {
            Assert.True(await billRepo.TryMarkPaidAsync(bill.Id, DateTime.Now));
            throw new InvalidOperationException("simulated failure after the bill was marked paid");
        }));

        Assert.Equal(BillStatus.Approved, (await ReloadBillAsync(bill.Id)).Status);
    }

    [Fact]
    public async Task Transaction_CommitsWhenTheWorkSucceeds()
    {
        var (_, bill) = await ArrangeAsync();
        var runner = GetService<ITransactionRunner>();
        var billRepo = GetService<IBillRepository>();

        var ok = await runner.ExecuteAsync(() => billRepo.TryMarkPaidAsync(bill.Id, DateTime.Now));

        Assert.True(ok);
        Assert.Equal(BillStatus.Paid, (await ReloadBillAsync(bill.Id)).Status);
    }

    // ------------------------------------------------------------------ constraints and round trips

    [Fact]
    public async Task TrackId_MustBeUnique_ButManyAttemptsMayHaveNone()
    {
        var (_, bill) = await ArrangeAsync();
        await AddAttemptAsync(bill.Id, PaymentAttemptStatus.Initiated, null);
        await AddAttemptAsync(bill.Id, PaymentAttemptStatus.Initiated, null);
        await AddAttemptAsync(bill.Id, PaymentAttemptStatus.Requested, 777);

        await Assert.ThrowsAsync<DbUpdateException>(() => AddAttemptAsync(bill.Id, PaymentAttemptStatus.Requested, 777));
    }

    [Fact]
    public async Task OrderId_MustBeUnique()
    {
        var (_, bill) = await ArrangeAsync();
        await AddAttemptAsync(bill.Id, PaymentAttemptStatus.Requested, 1, orderId: "same-order");

        await Assert.ThrowsAsync<DbUpdateException>(() => AddAttemptAsync(bill.Id, PaymentAttemptStatus.Requested, 2, orderId: "same-order"));
    }

    [Fact]
    public async Task UpdateAsync_RoundTripsEveryField()
    {
        var (_, bill) = await ArrangeAsync();
        var attempt = await AddAttemptAsync(bill.Id, PaymentAttemptStatus.Requested, 4242);
        var stamp = new DateTime(2025, 3, 20, 10, 30, 15);

        attempt.Status = PaymentAttemptStatus.Succeeded;
        attempt.RequestedAt = stamp;
        attempt.CallbackReceivedAt = stamp.AddMinutes(1);
        attempt.VerifiedAt = stamp.AddMinutes(2);
        attempt.PaidAt = stamp.AddSeconds(30);
        attempt.RequestResultCode = 100;
        attempt.CallbackSuccess = true;
        attempt.CallbackStatus = 2;
        attempt.ZibalStatus = 1;
        attempt.RefNumber = 123456789012;
        attempt.CardNumber = "6037****9999";
        attempt.PaidAmount = 500_000m;
        attempt.Note = "یادداشت";
        attempt.InitiatedByUserId = OwnerId;
        await Attempts.UpdateAsync(attempt);

        var stored = (await Attempts.GetByIdAsync(attempt.Id))!;
        Assert.Equal(PaymentAttemptStatus.Succeeded, stored.Status);
        Assert.Equal(stamp, stored.RequestedAt);
        Assert.Equal(stamp.AddMinutes(1), stored.CallbackReceivedAt);
        Assert.Equal(stamp.AddMinutes(2), stored.VerifiedAt);
        Assert.Equal(stamp.AddSeconds(30), stored.PaidAt);
        Assert.Equal(100, stored.RequestResultCode);
        Assert.True(stored.CallbackSuccess);
        Assert.Equal(2, stored.CallbackStatus);
        Assert.Equal(1, stored.ZibalStatus);
        Assert.Equal(123456789012, stored.RefNumber);
        Assert.Equal("6037****9999", stored.CardNumber);
        Assert.Equal(500_000m, stored.PaidAmount);
        Assert.Equal("یادداشت", stored.Note);
        Assert.Equal(OwnerId, stored.InitiatedByUserId);
        Assert.Equal(4242, stored.TrackId);
    }

    [Fact]
    public async Task Events_AreAppendedInOrder()
    {
        var (_, bill) = await ArrangeAsync();
        var attempt = await AddAttemptAsync(bill.Id, PaymentAttemptStatus.Requested, 5001);
        var t0 = DateTime.Now;
        await Attempts.AddEventAsync(new PaymentAttemptEvent { PaymentAttemptId = attempt.Id, CreatedAt = t0, EventType = PaymentEventType.Created, Message = "اول" });
        await Attempts.AddEventAsync(new PaymentAttemptEvent { PaymentAttemptId = attempt.Id, CreatedAt = t0.AddSeconds(1), EventType = PaymentEventType.Inquiry, ResultCode = 100, ZibalStatus = -1, Data = "{\"status\":-1}" });

        var loaded = (await Attempts.GetByPublicIdAsync(attempt.PublicId))!;

        Assert.Equal(2, loaded.Events.Count);
        Assert.Equal(PaymentEventType.Created, loaded.Events.First().EventType);
        Assert.Equal("{\"status\":-1}", loaded.Events.Last().Data);
        Assert.Equal(-1, loaded.Events.Last().ZibalStatus);
    }

    [Fact]
    public async Task HouseHistory_ContainsOnlyThatHousesAttempts()
    {
        var (house1, bill1) = await ArrangeAsync(title: "واحد 1", ownerId: "resident-1");
        var (house2, bill2) = await ArrangeAsync(title: "واحد 2", ownerId: "resident-2");
        await AddAttemptAsync(bill1.Id, PaymentAttemptStatus.Failed, 6001);
        await AddAttemptAsync(bill1.Id, PaymentAttemptStatus.Requested, 6002);
        await AddAttemptAsync(bill2.Id, PaymentAttemptStatus.Succeeded, 6003);

        var first = await Attempts.GetByHouseIdAsync(house1.Id);
        var second = await Attempts.GetByHouseIdAsync(house2.Id);
        var all = await Attempts.GetAllAsync();

        Assert.Equal(2, first.Count);
        Assert.All(first, a => Assert.Equal(bill1.Id, a.BillId));
        Assert.Single(second);
        Assert.Equal(3, all.Count);
        Assert.All(all, a => Assert.NotNull(a.Bill?.House?.Apartment));
    }

    [Fact]
    public async Task CountCreatedSince_OnlyCountsTheGivenBillInsideTheWindow()
    {
        var (_, bill) = await ArrangeAsync();
        var (_, other) = await ArrangeAsync(title: "واحد 2", ownerId: "resident-2");
        await AddAttemptAsync(bill.Id, PaymentAttemptStatus.Failed, 7001);
        await AddAttemptAsync(bill.Id, PaymentAttemptStatus.Failed, 7002);
        await AddAttemptAsync(other.Id, PaymentAttemptStatus.Failed, 7003);

        Assert.Equal(2, await Attempts.CountCreatedSinceAsync(bill.Id, DateTime.Now.AddMinutes(-10)));
        Assert.Equal(0, await Attempts.CountCreatedSinceAsync(bill.Id, DateTime.Now.AddMinutes(10)));
    }
}

/// <summary>The hand-written migration must produce a schema the EF model can use.</summary>
public class PaymentMigrationTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ServiceProvider _serviceProvider;

    public PaymentMigrationTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite("Data Source=:memory:"));
        services.AddIdentity<ApplicationUser, Microsoft.AspNetCore.Identity.IdentityRole>(options =>
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

    private async Task<List<string>> SqliteObjectNamesAsync()
    {
        var names = new List<string>();
        using var cmd = _db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table','index')";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    [Fact]
    public async Task Migrate_CreatesPaymentTablesAndIndexes()
    {
        await _db.Database.MigrateAsync();

        var names = await SqliteObjectNamesAsync();
        Assert.Contains("PaymentAttempts", names);
        Assert.Contains("PaymentAttemptEvents", names);
        Assert.Contains("IX_PaymentAttempts_PublicId", names);
        Assert.Contains("IX_PaymentAttempts_OrderId", names);
        Assert.Contains("IX_PaymentAttempts_TrackId", names);
    }

    [Fact]
    public async Task MigratedSchema_AcceptsAttemptsAndEventsWrittenThroughTheModel()
    {
        await _db.Database.MigrateAsync();

        var apartment = new Apartment { Title = "برج" };
        _db.Apartments.Add(apartment);
        await _db.SaveChangesAsync();
        var house = new House { Title = "واحد 1", ApartmentId = apartment.Id, ResidentName = "ساکن", ResidentPhoneNumber = "09120000000", IsActive = true };
        _db.Houses.Add(house);
        await _db.SaveChangesAsync();
        var bill = new Bill { HouseId = house.Id, Year = 1404, Month = 1, TotalAmount = 500_000m, Status = BillStatus.Approved, CreatedDate = DateTime.Now };
        _db.Bills.Add(bill);
        await _db.SaveChangesAsync();

        var attempt = new PaymentAttempt
        {
            BillId = bill.Id,
            Amount = 500_000m,
            OrderId = Guid.NewGuid().ToString("N"),
            TrackId = 123456789012345,
            Status = PaymentAttemptStatus.Requested,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            CardNumber = "6037****1234",
            PaidAmount = 500_000m,
            RefNumber = 99
        };
        attempt.Events.Add(new PaymentAttemptEvent { CreatedAt = DateTime.Now, EventType = PaymentEventType.Created, Message = "m", Data = "{}" });
        _db.PaymentAttempts.Add(attempt);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        var stored = await _db.PaymentAttempts.Include(a => a.Events).SingleAsync();
        Assert.Equal(123456789012345, stored.TrackId);
        Assert.Equal(500_000m, stored.Amount);
        Assert.Single(stored.Events);

        _db.PaymentAttempts.Add(new PaymentAttempt
        {
            BillId = bill.Id,
            Amount = 1m,
            OrderId = Guid.NewGuid().ToString("N"),
            TrackId = 123456789012345,   // duplicate gateway session
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
