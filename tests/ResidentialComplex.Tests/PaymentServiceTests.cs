using ResidentialComplex.Application.DTOs;
using ResidentialComplex.Application.Services;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Domain.Enums;
using Xunit;

namespace ResidentialComplex.Tests;

/// <summary>Arranges one resident ("u1") with an approved 500,000 Rial bill and a fake Zibal.</summary>
public class PaymentEnv
{
    public const string CallbackUrl = "https://site.example/payment/callback";

    public FakeStore Store = new();
    public FakePaymentGateway Gateway = new();
    public FakeAudit Audit = new();
    public FakeClock Clock = new();
    public FakeHouses Houses;
    public FakeAttempts Attempts;
    public PaymentService Service;
    public House House;
    public Bill Bill;

    public PaymentEnv(decimal amount = 500_000)
    {
        Houses = new FakeHouses(Store);
        Attempts = new FakeAttempts(Store);
        House = new House { Id = 1, Title = "واحد 1", ResidentPhoneNumber = "۰۹۱۲۳۴۵۶۷۸۹", ApplicationUserId = "u1", CurrentDebt = amount };
        Bill = new Bill { Id = 10, HouseId = 1, House = House, Year = 1404, Month = 1, TotalAmount = amount, Status = BillStatus.Approved };
        Store.Houses.Add(House);
        Store.Bills.Add(Bill);
        Service = new PaymentService(Attempts, new FakeBills(Store), Houses, new FakePayments(Store), Gateway, new FakeTx(Store), Audit, Clock);
    }

    public Task<StartPaymentResult> StartAsync(string user = "u1") => Service.StartPaymentAsync(10, user, user, CallbackUrl);

    public PaymentAttempt AttemptOf(StartPaymentResult r) => Store.Attempts.First(a => a.PublicId == r.AttemptPublicId);
    public long TrackOf(StartPaymentResult r) => AttemptOf(r).TrackId!.Value;

    public Task<PaymentProcessingResult> CallbackAsync(long track, string success = "1", string? orderId = null) =>
        Service.HandleCallbackAsync(new PaymentCallback(track, success, success == "1" ? "2" : "3",
            orderId ?? Store.Attempts.First(a => a.TrackId == track).OrderId, $"?trackId={track}&success={success}"));

    public Task<PaymentProcessingResult> ReconcileAsync(PaymentAttempt a) => Service.ReconcileAsync(a.Id, "u1", "u1");
}

public class PaymentServiceTests
{
    // ---------------------------------------------------------------- happy path

    [Fact]
    public async Task Start_CreatesAttempt_AndRedirectsToGateway()
    {
        var e = new PaymentEnv();
        var start = await e.StartAsync();

        Assert.True(start.Success);
        Assert.StartsWith("https://gateway.zibal.ir/start/", start.RedirectUrl);
        Assert.Equal(500_000, e.Gateway.LastRequest!.AmountRial);
        Assert.Equal("09123456789", e.Gateway.LastRequest.Mobile);
        Assert.Equal(PaymentAttemptStatus.Requested, e.AttemptOf(start).Status);
        Assert.NotNull(e.AttemptOf(start).TrackId);
        Assert.Equal(32, e.AttemptOf(start).OrderId.Length);
    }

    [Fact]
    public async Task Callback_ForPaidSession_SettlesTheBillExactlyOnce()
    {
        var e = new PaymentEnv();
        var start = await e.StartAsync();
        var track = e.TrackOf(start);
        e.Gateway.Pay(track);

        var result = await e.CallbackAsync(track);

        Assert.Equal(PaymentProcessingOutcome.Succeeded, result.Outcome);
        Assert.Equal(BillStatus.Paid, e.Bill.Status);
        Assert.NotNull(e.Bill.PaidDate);
        Assert.Single(e.Store.Payments);
        Assert.Equal(500_000, e.Store.Payments[0].Amount);
        Assert.Contains(track.ToString(), e.Store.Payments[0].Description);
        Assert.Equal(0, e.House.CurrentDebt);
        Assert.Equal(1, e.Gateway.VerifyCalls);

        var attempt = e.AttemptOf(start);
        Assert.Equal(PaymentAttemptStatus.Succeeded, attempt.Status);
        Assert.Equal(e.Store.Payments[0].Id, attempt.PaymentId);
        Assert.Equal(987654, attempt.RefNumber);
        Assert.Equal("6037****1234", attempt.CardNumber);
        Assert.Equal(500_000, attempt.PaidAmount);
        Assert.NotNull(attempt.VerifiedAt);
    }

    [Fact]
    public async Task EveryGatewayInteractionIsStoredAsAnEvent()
    {
        var e = new PaymentEnv();
        var start = await e.StartAsync();
        e.Gateway.Pay(e.TrackOf(start));
        await e.CallbackAsync(e.TrackOf(start));

        var types = e.Store.Events.Select(x => x.EventType).ToHashSet();
        Assert.Contains(PaymentEventType.Created, types);
        Assert.Contains(PaymentEventType.GatewayRequest, types);
        Assert.Contains(PaymentEventType.Callback, types);
        Assert.Contains(PaymentEventType.Inquiry, types);
        Assert.Contains(PaymentEventType.Verify, types);
        Assert.Contains(PaymentEventType.Settled, types);
        Assert.Contains("PaymentStarted", e.Audit.Actions);
        Assert.Contains("OnlinePaymentConfirmed", e.Audit.Actions);
    }

    [Fact]
    public async Task ReplayedCallback_DoesNotCreditTwice()
    {
        var e = new PaymentEnv();
        var start = await e.StartAsync();
        var track = e.TrackOf(start);
        e.Gateway.Pay(track);
        await e.CallbackAsync(track);

        var replay = await e.CallbackAsync(track);

        Assert.Equal(PaymentProcessingOutcome.AlreadyFinal, replay.Outcome);
        Assert.Single(e.Store.Payments);
        Assert.Equal(1, e.Gateway.VerifyCalls);
        Assert.Equal(0, e.House.CurrentDebt);
    }

    [Fact]
    public async Task StartOnAnAlreadyPaidBill_IsRefused()
    {
        var e = new PaymentEnv();
        var start = await e.StartAsync();
        e.Gateway.Pay(e.TrackOf(start));
        await e.CallbackAsync(e.TrackOf(start));

        var again = await e.StartAsync();

        Assert.False(again.Success);
        Assert.True(again.AlreadyPaid);
    }

    // ---------------------------------------------------------------- untrusted callbacks

    [Fact]
    public async Task ForgedSuccessCallback_ForAnUnpaidSession_ChangesNothing()
    {
        var e = new PaymentEnv();
        var start = await e.StartAsync();
        var track = e.TrackOf(start);

        var result = await e.CallbackAsync(track, "1");

        Assert.Equal(PaymentProcessingOutcome.Pending, result.Outcome);
        Assert.Equal(BillStatus.Approved, e.Bill.Status);
        Assert.Empty(e.Store.Payments);
        Assert.Equal(PaymentAttemptStatus.CallbackReceived, e.AttemptOf(start).Status);
        Assert.Equal(0, e.Gateway.VerifyCalls);
    }

    [Fact]
    public async Task ForgedFailureCallback_DoesNotStopARealPayment()
    {
        var e = new PaymentEnv();
        var start = await e.StartAsync();
        var track = e.TrackOf(start);
        e.Gateway.Pay(track);

        var result = await e.CallbackAsync(track, "0");   // says "failed" although Zibal says paid

        Assert.Equal(PaymentProcessingOutcome.Succeeded, result.Outcome);
        Assert.Equal(BillStatus.Paid, e.Bill.Status);
    }

    [Fact]
    public async Task WrongOrderIdInCallback_IsOnlyLogged()
    {
        var e = new PaymentEnv();
        var start = await e.StartAsync();
        var track = e.TrackOf(start);
        e.Gateway.Pay(track);

        var result = await e.Service.HandleCallbackAsync(new PaymentCallback(track, "1", "2", "attacker-order", "?x"));

        Assert.Equal(PaymentProcessingOutcome.Succeeded, result.Outcome);
        Assert.Contains(e.Store.Events, x => x.EventType == PaymentEventType.Warning);
    }

    [Fact]
    public async Task UnknownTrackId_CreatesNothing()
    {
        var e = new PaymentEnv();
        await e.StartAsync();

        var result = await e.Service.HandleCallbackAsync(new PaymentCallback(424242, "1", "2", "o", "?"));

        Assert.Equal(PaymentProcessingOutcome.UnknownTrack, result.Outcome);
        Assert.Null(result.AttemptPublicId);
        Assert.Single(e.Store.Attempts);
    }

    // ---------------------------------------------------------------- failures and mismatches

    [Fact]
    public async Task CancelledByUser_MarksAttemptFailed_AndAllowsRetry()
    {
        var e = new PaymentEnv();
        var start = await e.StartAsync();
        var track = e.TrackOf(start);
        e.Gateway.Pay(track, 3);

        var result = await e.CallbackAsync(track, "0");

        Assert.Equal(PaymentProcessingOutcome.Failed, result.Outcome);
        Assert.Equal(PaymentAttemptStatus.Failed, e.AttemptOf(start).Status);
        Assert.Equal(3, e.AttemptOf(start).ZibalStatus);
        Assert.Equal(BillStatus.Approved, e.Bill.Status);

        var retry = await e.StartAsync();
        Assert.True(retry.Success);
        Assert.Equal(2, e.Store.Attempts.Count);
        Assert.NotEqual(track, e.TrackOf(retry));
    }

    [Fact]
    public async Task AmountMismatch_NeedsReview_AndDoesNotPayTheBill()
    {
        var e = new PaymentEnv();
        var start = await e.StartAsync();
        var track = e.TrackOf(start);
        e.Gateway.Pay(track);
        e.Gateway.AmountOverride = 1_000;

        var result = await e.CallbackAsync(track);

        Assert.Equal(PaymentProcessingOutcome.NeedsReview, result.Outcome);
        Assert.Equal(BillStatus.Approved, e.Bill.Status);
        Assert.Empty(e.Store.Payments);
        Assert.Equal(500_000, e.House.CurrentDebt);
        Assert.Equal(PaymentAttemptStatus.NeedsReview, e.AttemptOf(start).Status);
        Assert.Equal(1_000, e.AttemptOf(start).PaidAmount);
        Assert.Contains("مطابقت", e.AttemptOf(start).Note);
    }

    [Fact]
    public async Task OrderIdMismatchInVerifiedData_NeedsReview()
    {
        var e = new PaymentEnv();
        var start = await e.StartAsync();
        var track = e.TrackOf(start);
        e.Gateway.Pay(track);
        e.Gateway.OrderIdOverride = "someone-elses-order";

        var result = await e.CallbackAsync(track);

        Assert.Equal(PaymentProcessingOutcome.NeedsReview, result.Outcome);
        Assert.Equal(BillStatus.Approved, e.Bill.Status);
    }

    [Fact]
    public async Task PayingTheSameBillTwice_FirstSucceeds_SecondIsFlaggedForRefund()
    {
        var e = new PaymentEnv();
        var s1 = await e.StartAsync();
        var s2 = await e.StartAsync();   // two open tabs
        e.Gateway.Pay(e.TrackOf(s1));
        e.Gateway.Pay(e.TrackOf(s2));

        var r1 = await e.CallbackAsync(e.TrackOf(s1));
        var r2 = await e.CallbackAsync(e.TrackOf(s2));

        Assert.Equal(PaymentProcessingOutcome.Succeeded, r1.Outcome);
        Assert.Equal(PaymentProcessingOutcome.NeedsReview, r2.Outcome);
        Assert.Single(e.Store.Payments);
        Assert.Equal(0, e.House.CurrentDebt);
        Assert.Contains("قبلاً پرداخت", e.AttemptOf(s2).Note);
    }

    [Fact]
    public async Task VerifyResult201_AlreadyVerified_IsAcceptedAsPaid()
    {
        var e = new PaymentEnv();
        var start = await e.StartAsync();
        e.Gateway.Pay(e.TrackOf(start));
        e.Gateway.VerifyReturns201 = true;

        var result = await e.CallbackAsync(e.TrackOf(start));

        Assert.Equal(PaymentProcessingOutcome.Succeeded, result.Outcome);
        Assert.Equal(BillStatus.Paid, e.Bill.Status);
    }

    [Fact]
    public async Task InquiryStatus1_SettlesWithoutCallingVerify()
    {
        var e = new PaymentEnv();
        var start = await e.StartAsync();
        e.Gateway.Pay(e.TrackOf(start), 1);

        var result = await e.CallbackAsync(e.TrackOf(start));

        Assert.Equal(PaymentProcessingOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, e.Gateway.VerifyCalls);
    }

    // ---------------------------------------------------------------- start validation / security

    [Fact]
    public async Task AnotherUser_CannotPayTheBill()
    {
        var e = new PaymentEnv();

        var result = await e.StartAsync("u2");

        Assert.False(result.Success);
        Assert.Empty(e.Store.Attempts);
        Assert.Contains("PaymentStartDenied", e.Audit.Actions);
    }

    [Fact]
    public async Task HouseWithoutLinkedUser_CannotBePaidByAnyone()
    {
        var e = new PaymentEnv();
        e.House.ApplicationUserId = null;

        Assert.False((await e.StartAsync("u1")).Success);
        Assert.Empty(e.Store.Attempts);
    }

    [Fact]
    public async Task DraftBill_IsNotPayable()
    {
        var e = new PaymentEnv();
        e.Bill.Status = BillStatus.Draft;

        Assert.False((await e.StartAsync()).Success);
    }

    [Fact]
    public async Task UnconfiguredGateway_RefusesToStart()
    {
        var e = new PaymentEnv();
        e.Gateway.Configured = false;

        var result = await e.StartAsync();

        Assert.False(result.Success);
        Assert.Contains("پیکربندی", result.ErrorMessage);
    }

    [Fact]
    public async Task NonHttpCallbackUrl_IsRefused()
    {
        var e = new PaymentEnv();

        var result = await e.Service.StartPaymentAsync(10, "u1", "u1", "javascript:alert(1)");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task AmountsAtOrBelow1000Rial_OrWithFraction_AreRefused()
    {
        Assert.False((await new PaymentEnv(500).StartAsync()).Success);

        var fractional = new PaymentEnv();
        fractional.Bill.TotalAmount = 12345.5m;
        Assert.False((await fractional.StartAsync()).Success);
    }

    [Fact]
    public async Task GatewayRejectingTheRequest_IsStoredAsRequestFailed()
    {
        var e = new PaymentEnv();
        e.Gateway.RequestResult = 105;

        var result = await e.StartAsync();

        Assert.False(result.Success);
        Assert.Null(result.RedirectUrl);
        Assert.Equal(PaymentAttemptStatus.RequestFailed, e.Store.Attempts[0].Status);
        Assert.Equal(105, e.Store.Attempts[0].RequestResultCode);
        Assert.Contains(e.Store.Events, x => x.EventType == PaymentEventType.GatewayRequest);
    }

    [Fact]
    public async Task GatewayUnreachableOnRequest_IsStoredAsRequestFailed()
    {
        var e = new PaymentEnv();
        e.Gateway.RequestTransportError = true;

        var result = await e.StartAsync();

        Assert.False(result.Success);
        Assert.Equal(PaymentAttemptStatus.RequestFailed, e.Store.Attempts[0].Status);
    }

    [Fact]
    public async Task AttemptsPerBill_AreRateLimited()
    {
        var e = new PaymentEnv();
        var ok = 0;
        for (var i = 0; i < 6; i++)
        {
            if ((await e.StartAsync()).Success) ok++;
        }

        Assert.Equal(PaymentService.MaxAttemptsPerWindow, ok);
        Assert.Equal(PaymentService.MaxAttemptsPerWindow, e.Store.Attempts.Count);

        e.Clock.Advance(PaymentService.AttemptWindow + TimeSpan.FromMinutes(1));
        Assert.True((await e.StartAsync()).Success);
    }

    [Fact]
    public async Task OrderIds_AreUniquePerAttempt()
    {
        var e = new PaymentEnv();
        await e.StartAsync();
        e.Clock.Advance(TimeSpan.FromMinutes(11));
        await e.StartAsync();

        Assert.NotEqual(e.Store.Attempts[0].OrderId, e.Store.Attempts[1].OrderId);
    }

    // ---------------------------------------------------------------- concurrency

    [Fact]
    public async Task ParallelCallbacks_SettleExactlyOnce()
    {
        var e = new PaymentEnv();
        var start = await e.StartAsync();
        var track = e.TrackOf(start);
        e.Gateway.Pay(track);
        e.Gateway.YieldInInquiry = true;

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => e.CallbackAsync(track))));

        Assert.Equal(1, results.Count(r => r.Outcome == PaymentProcessingOutcome.Succeeded));
        Assert.All(results, r => Assert.True(r.Outcome is PaymentProcessingOutcome.Succeeded
            or PaymentProcessingOutcome.InProgress or PaymentProcessingOutcome.AlreadyFinal));
        Assert.Equal(1, e.Gateway.VerifyCalls);
        Assert.Single(e.Store.Payments);
        Assert.Equal(0, e.House.CurrentDebt);
    }

    // ---------------------------------------------------------------- resilience

    [Fact]
    public async Task GatewayDownDuringInquiry_ReleasesTheAttempt_AndRetryCompletes()
    {
        var e = new PaymentEnv();
        var start = await e.StartAsync();
        var track = e.TrackOf(start);
        e.Gateway.Pay(track);
        e.Gateway.InquiryTransportErrors = 1;

        var first = await e.CallbackAsync(track);
        Assert.Equal(PaymentProcessingOutcome.TemporaryError, first.Outcome);
        Assert.Equal(PaymentAttemptStatus.CallbackReceived, e.AttemptOf(start).Status);

        var retry = await e.ReconcileAsync(e.AttemptOf(start));
        Assert.Equal(PaymentProcessingOutcome.Succeeded, retry.Outcome);
        Assert.Equal(BillStatus.Paid, e.Bill.Status);
    }

    [Fact]
    public async Task VerifyCallFailing_LeavesBillUntouched_AndRetryVerifiesAgain()
    {
        var e = new PaymentEnv();
        var start = await e.StartAsync();
        e.Gateway.Pay(e.TrackOf(start));
        e.Gateway.VerifyTransportErrors = 1;

        var first = await e.CallbackAsync(e.TrackOf(start));
        Assert.Equal(PaymentProcessingOutcome.TemporaryError, first.Outcome);
        Assert.Equal(BillStatus.Approved, e.Bill.Status);

        var retry = await e.ReconcileAsync(e.AttemptOf(start));
        Assert.Equal(PaymentProcessingOutcome.Succeeded, retry.Outcome);
        Assert.Equal(2, e.Gateway.VerifyCalls);
    }

    [Fact]
    public async Task DatabaseFailureWhileSettling_RollsBack_AndRetrySettlesWithoutSecondVerify()
    {
        var e = new PaymentEnv();
        var start = await e.StartAsync();
        e.Gateway.Pay(e.TrackOf(start));
        e.Houses.ThrowOnUpdate = true;

        var first = await e.CallbackAsync(e.TrackOf(start));

        Assert.Equal(PaymentProcessingOutcome.TemporaryError, first.Outcome);
        Assert.Equal(BillStatus.Approved, e.Bill.Status);            // rolled back
        Assert.Empty(e.Store.Payments);                              // rolled back
        Assert.Equal(PaymentAttemptStatus.Verified, e.AttemptOf(start).Status);

        e.Houses.ThrowOnUpdate = false;
        var verifyCallsBefore = e.Gateway.VerifyCalls;
        var retry = await e.ReconcileAsync(e.AttemptOf(start));

        Assert.Equal(PaymentProcessingOutcome.Succeeded, retry.Outcome);
        Assert.Equal(verifyCallsBefore, e.Gateway.VerifyCalls);      // Zibal already says status=1
        Assert.Equal(BillStatus.Paid, e.Bill.Status);
        Assert.Single(e.Store.Payments);
        Assert.Equal(0, e.House.CurrentDebt);
    }

    [Fact]
    public async Task UnpaidAfterAnHour_BecomesExpired()
    {
        var e = new PaymentEnv();
        var start = await e.StartAsync();
        e.Clock.Advance(PaymentService.ExpireAfter + TimeSpan.FromMinutes(1));

        var result = await e.ReconcileAsync(e.AttemptOf(start));

        Assert.Equal(PaymentAttemptStatus.Expired, e.AttemptOf(start).Status);
        Assert.Equal(PaymentProcessingOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task FreshVerifyingLease_IsRespected_StaleLeaseIsReclaimed()
    {
        var e = new PaymentEnv();
        var start = await e.StartAsync();
        var track = e.TrackOf(start);
        var attempt = e.AttemptOf(start);
        attempt.Status = PaymentAttemptStatus.Verifying;
        attempt.UpdatedAt = e.Clock.GetLocalNow().DateTime;   // another worker is busy right now

        var busy = await e.ReconcileAsync(attempt);
        Assert.Equal(PaymentProcessingOutcome.InProgress, busy.Outcome);
        Assert.Equal(0, e.Gateway.InquiryCalls);

        e.Clock.Advance(PaymentService.ClaimLease + TimeSpan.FromMinutes(1));
        e.Gateway.Pay(track);
        var reclaimed = await e.ReconcileAsync(attempt);
        Assert.Equal(PaymentProcessingOutcome.Succeeded, reclaimed.Outcome);
    }

    [Fact]
    public async Task StartingAgain_ReconcilesAnEarlierPaidAttempt_InsteadOfOpeningASecondSession()
    {
        var e = new PaymentEnv();
        var first = await e.StartAsync();
        e.Gateway.Pay(e.TrackOf(first));          // paid at the bank, callback never arrived

        var second = await e.StartAsync();

        Assert.False(second.Success);
        Assert.True(second.AlreadyPaid);
        Assert.Equal(BillStatus.Paid, e.Bill.Status);
        Assert.Single(e.Store.Attempts);
    }

    [Fact]
    public async Task InitiatedAttemptThatNeverGotATrackId_IsClosedAsRequestFailed()
    {
        var e = new PaymentEnv();
        await e.StartAsync();
        var attempt = e.Store.Attempts[0];
        attempt.Status = PaymentAttemptStatus.Initiated;
        attempt.TrackId = null;
        e.Clock.Advance(PaymentService.InitiatedTimeout + TimeSpan.FromMinutes(1));

        await e.ReconcileAsync(attempt);

        Assert.Equal(PaymentAttemptStatus.RequestFailed, e.Store.Attempts[0].Status);
    }

    // ---------------------------------------------------------------- ownership of results

    [Fact]
    public async Task OnlyTheOwnerOrAnAdministrator_CanReadAnAttempt()
    {
        var e = new PaymentEnv();
        var start = await e.StartAsync();
        var id = start.AttemptPublicId!.Value;

        Assert.NotNull(await e.Service.GetForUserAsync(id, "u1", false));
        Assert.Null(await e.Service.GetForUserAsync(id, "u2", false));
        Assert.NotNull(await e.Service.GetForUserAsync(id, "admin", true));
        Assert.Null(await e.Service.GetForUserAsync(Guid.NewGuid(), "u1", true));
    }

    [Fact]
    public async Task ReconcileForUser_EnforcesOwnership_BeforeAnyGatewayCall()
    {
        var e = new PaymentEnv();
        var start = await e.StartAsync();
        var id = start.AttemptPublicId!.Value;
        e.Gateway.Pay(e.TrackOf(start));

        var stranger = await e.Service.ReconcileForUserAsync(id, "u2", "u2", isAdministrator: false);
        Assert.Equal(PaymentProcessingOutcome.UnknownTrack, stranger.Outcome);
        Assert.Equal(0, e.Gateway.InquiryCalls);
        Assert.Equal(BillStatus.Approved, e.Bill.Status);

        var owner = await e.Service.ReconcileForUserAsync(id, "u1", "u1", isAdministrator: false);
        Assert.Equal(PaymentProcessingOutcome.Succeeded, owner.Outcome);
        Assert.Equal(BillStatus.Paid, e.Bill.Status);
    }

    // ---------------------------------------------------------------- helpers

    [Theory]
    [InlineData("09123456789", "09123456789")]
    [InlineData("+98 912 345 6789", "09123456789")]
    [InlineData("00989123456789", "09123456789")]
    [InlineData("۰۹۱۲۳۴۵۶۷۸۹", "09123456789")]
    [InlineData("9123456789", "09123456789")]
    [InlineData("021-1234", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeMobile_HandlesIranianFormats(string? input, string? expected)
    {
        Assert.Equal(expected, PaymentService.NormalizeMobile(input));
    }
}
