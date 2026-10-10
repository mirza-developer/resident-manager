using ResidentialComplex.Application.DTOs;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Domain.Enums;

namespace ResidentialComplex.Tests;

// =====================================================================================
// In-memory fakes used by the payment tests that must not need a database
// (PaymentServiceTests). They mimic the contracts of the real EF repositories:
// detached copies on read, atomic conditional updates (Try*), transaction rollback.
// FakePaymentGateway behaves like Zibal as documented in Docs/Zibal-docs.json.
// =====================================================================================

public class FakeStore
{
    public readonly object Gate = new();
    public List<Bill> Bills = new();
    public List<House> Houses = new();
    public List<Payment> Payments = new();
    public List<PaymentAttempt> Attempts = new();
    public List<PaymentAttemptEvent> Events = new();
    int _pid = 0, _aid = 0, _eid = 0;
    public int NextPaymentId() => ++_pid; public int NextAttemptId() => ++_aid; public int NextEventId() => ++_eid;

    public static T Clone<T>(T x) where T : class => (T)typeof(T).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(x, null)!;

    // transaction emulation: snapshot scalar state, restore on failure
    public (List<(Bill, BillStatus, DateTime?)>, List<(House, decimal)>, int, int, int) Snap() =>
        (Bills.Select(b => (b, b.Status, b.PaidDate)).ToList(), Houses.Select(h => (h, h.CurrentDebt)).ToList(), Payments.Count, Attempts.Count, Events.Count);
    public void Restore((List<(Bill, BillStatus, DateTime?)>, List<(House, decimal)>, int, int, int) s)
    {
        foreach (var (b, st, pd) in s.Item1) { b.Status = st; b.PaidDate = pd; }
        foreach (var (h, d) in s.Item2) h.CurrentDebt = d;
        Payments.RemoveRange(s.Item3, Payments.Count - s.Item3);
    }
}

public class FakeBills : IBillRepository
{
    readonly FakeStore s; public FakeBills(FakeStore s) => this.s = s;
    public Task<Bill?> GetByIdAsync(int id) { lock (s.Gate) return Task.FromResult(s.Bills.FirstOrDefault(b => b.Id == id)); }
    public Task<bool> TryMarkPaidAsync(int billId, DateTime paidDate)
    { lock (s.Gate) { var b = s.Bills.FirstOrDefault(x => x.Id == billId); if (b is null || b.Status != BillStatus.Approved) return Task.FromResult(false); b.Status = BillStatus.Paid; b.PaidDate = paidDate; return Task.FromResult(true); } }
    public Task<List<Bill>> GetAllAsync() => throw new NotImplementedException();
    public Task<List<Bill>> GetByHouseIdAsync(int houseId) => throw new NotImplementedException();
    public Task<List<Bill>> GetByMonthYearAsync(int year, int month) => throw new NotImplementedException();
    public Task<Bill?> GetByHouseMonthYearAsync(int houseId, int year, int month) => throw new NotImplementedException();
    public Task<Bill> AddAsync(Bill bill) => throw new NotImplementedException();
    public Task AddRangeAsync(IEnumerable<Bill> bills) => throw new NotImplementedException();
    public Task UpdateAsync(Bill bill) => throw new NotImplementedException();
    public Task DeleteAsync(int id) => throw new NotImplementedException();
    public Task<List<Bill>> GetForReportAsync(int? year, int? month, int? houseId) => throw new NotImplementedException();
    public decimal CalculateEqualDivisionAmount(decimal totalAmount, int houseCount) => throw new NotImplementedException();
    public Task<decimal> CalculateBracketAmountAsync(FinancialItem fi, int houseId, int year, int month) => throw new NotImplementedException();
}

public class FakeHouses : IHouseRepository
{
    readonly FakeStore s; public bool ThrowOnUpdate; public FakeHouses(FakeStore s) => this.s = s;
    public Task<House?> GetByIdAsync(int id) { lock (s.Gate) return Task.FromResult(s.Houses.FirstOrDefault(h => h.Id == id)); }
    public Task UpdateAsync(House house) { if (ThrowOnUpdate) throw new InvalidOperationException("db down"); return Task.CompletedTask; }
    public Task<List<House>> GetAllAsync() => throw new NotImplementedException();
    public Task<List<House>> GetByApartmentIdAsync(int apartmentId) => throw new NotImplementedException();
    public Task<List<House>> GetActiveHousesAsync() => throw new NotImplementedException();
    public Task<House?> GetByUserIdAsync(string userId) => throw new NotImplementedException();
    public Task<House> AddAsync(House house) => throw new NotImplementedException();
    public Task DeleteAsync(int id) => throw new NotImplementedException();
}

public class FakePayments : IPaymentRepository
{
    readonly FakeStore s; public FakePayments(FakeStore s) => this.s = s;
    public Task<Payment> AddAsync(Payment p) { lock (s.Gate) { p.Id = s.NextPaymentId(); s.Payments.Add(p); return Task.FromResult(p); } }
    public Task<List<Payment>> GetAllAsync() => throw new NotImplementedException();
    public Task<List<Payment>> GetByBillIdAsync(int billId) => throw new NotImplementedException();
    public Task<Payment?> GetByIdAsync(int id) => throw new NotImplementedException();
}

public class FakeTx : ITransactionRunner
{
    readonly FakeStore s; public FakeTx(FakeStore s) => this.s = s;
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> work)
    { var snap = s.Snap(); try { return await work(); } catch { s.Restore(snap); throw; } }
}

public class FakeAudit : IAuditService
{
    public List<string> Actions = new();
    public Task LogAsync(string userId, string userName, string entityName, string entityId, string action, string? o, string? n) { lock (Actions) Actions.Add(action); return Task.CompletedTask; }
}

/// <summary>Detached-copy semantics + atomic conditional updates, like the EF repository.</summary>
public class FakeAttempts : IPaymentAttemptRepository
{
    readonly FakeStore s; public FakeAttempts(FakeStore s) => this.s = s;
    PaymentAttempt Copy(PaymentAttempt a) => FakeStore.Clone(a);
    public Task<PaymentAttempt> AddAsync(PaymentAttempt a) { lock (s.Gate) { a.Id = s.NextAttemptId(); s.Attempts.Add(Copy(a)); return Task.FromResult(a); } }
    public Task<PaymentAttempt?> GetByIdAsync(int id) { lock (s.Gate) { var a = s.Attempts.FirstOrDefault(x => x.Id == id); return Task.FromResult(a is null ? null : Copy(a)); } }
    public Task<PaymentAttempt?> GetByPublicIdAsync(Guid id) { lock (s.Gate) { var a = s.Attempts.FirstOrDefault(x => x.PublicId == id); if (a is null) return Task.FromResult<PaymentAttempt?>(null); var c = Copy(a); c.Bill = s.Bills.First(b => b.Id == a.BillId); c.Events = s.Events.Where(e => e.PaymentAttemptId == a.Id).ToList(); return Task.FromResult<PaymentAttempt?>(c); } }
    public Task<PaymentAttempt?> GetByTrackIdAsync(long t) { lock (s.Gate) { var a = s.Attempts.FirstOrDefault(x => x.TrackId == t); return Task.FromResult(a is null ? null : Copy(a)); } }
    public Task<List<PaymentAttempt>> GetByBillIdAsync(int billId) { lock (s.Gate) return Task.FromResult(s.Attempts.Where(a => a.BillId == billId).Select(Copy).ToList()); }
    public Task<List<PaymentAttempt>> GetByHouseIdAsync(int houseId) => throw new NotImplementedException();
    public Task<List<PaymentAttempt>> GetAllAsync() => throw new NotImplementedException();
    public Task<int> CountCreatedSinceAsync(int billId, DateTime since) { lock (s.Gate) return Task.FromResult(s.Attempts.Count(a => a.BillId == billId && a.CreatedAt >= since)); }
    public Task UpdateAsync(PaymentAttempt a) { lock (s.Gate) { var i = s.Attempts.FindIndex(x => x.Id == a.Id); s.Attempts[i] = Copy(a); return Task.CompletedTask; } }
    public Task<bool> TryClaimAsync(int id, DateTime now, DateTime staleBefore)
    { lock (s.Gate) { var a = s.Attempts.First(x => x.Id == id);
        if (a.Status is PaymentAttemptStatus.Requested or PaymentAttemptStatus.CallbackReceived or PaymentAttemptStatus.Verified || (a.Status == PaymentAttemptStatus.Verifying && a.UpdatedAt < staleBefore))
        { a.Status = PaymentAttemptStatus.Verifying; a.UpdatedAt = now; return Task.FromResult(true); } return Task.FromResult(false); } }
    public Task<bool> TryReleaseAsync(int id, PaymentAttemptStatus st, string? note, DateTime now)
    { lock (s.Gate) { var a = s.Attempts.First(x => x.Id == id); if (a.Status != PaymentAttemptStatus.Verifying) return Task.FromResult(false); a.Status = st; a.Note = note; a.UpdatedAt = now; return Task.FromResult(true); } }
    public Task<bool> TryMarkCallbackReceivedAsync(int id, bool? success, int? status, DateTime now)
    { lock (s.Gate) { var a = s.Attempts.First(x => x.Id == id); if (a.Status != PaymentAttemptStatus.Requested) return Task.FromResult(false); a.Status = PaymentAttemptStatus.CallbackReceived; a.CallbackReceivedAt = now; a.CallbackSuccess = success; a.CallbackStatus = status; a.UpdatedAt = now; return Task.FromResult(true); } }
    public Task AddEventAsync(PaymentAttemptEvent e) { lock (s.Gate) { e.Id = s.NextEventId(); s.Events.Add(e); return Task.CompletedTask; } }
}

public class FakeZibalSession { public long TrackId; public string OrderId = ""; public long Amount; public int Status = -1; public long? Ref; public string? Card; }

/// <summary>Behaves like Zibal per Docs/Zibal-docs.json (request/inquiry/verify result + status codes).</summary>
public class FakePaymentGateway : IPaymentGateway
{
    public bool VerifyReturns201; public bool Configured = true; public int RequestResult = 100; public bool RequestTransportError;
    public int InquiryTransportErrors, VerifyTransportErrors; public int VerifyCalls, InquiryCalls, RequestCalls;
    public long? AmountOverride; public string? OrderIdOverride; public bool YieldInInquiry;
    public Dictionary<long, FakeZibalSession> Sessions = new(); long _next = 1000;
    public GatewayRequest? LastRequest;
    public string Name => "Zibal"; public bool IsConfigured => Configured;
    public string GetStartUrl(long t) => $"https://gateway.zibal.ir/start/{t}";
    public Task<GatewayRequestResult> RequestPaymentAsync(GatewayRequest r, CancellationToken ct = default)
    {
        RequestCalls++; LastRequest = r;
        if (RequestTransportError) return Task.FromResult(new GatewayRequestResult { TransportError = true });
        if (RequestResult != 100) return Task.FromResult(new GatewayRequestResult { ResultCode = RequestResult, Message = "x", RawData = "{\"result\":" + RequestResult + "}" });
        var t = ++_next; Sessions[t] = new FakeZibalSession { TrackId = t, OrderId = r.OrderId, Amount = r.AmountRial };
        return Task.FromResult(new GatewayRequestResult { ResultCode = 100, TrackId = t, RawData = "{\"result\":100}" });
    }
    public FakeZibalSession Pay(long track, int status = 2) { var z = Sessions[track]; z.Status = status; if (status is 1 or 2) { z.Ref = 987654; z.Card = "6037****1234"; } return z; }
    GatewayTransactionInfo Info(FakeZibalSession z, int result) => new()
    { ResultCode = result, Status = z.Status, Amount = AmountOverride ?? z.Amount, OrderId = OrderIdOverride ?? z.OrderId, RefNumber = z.Ref, CardNumber = z.Card, PaidAt = new DateTime(2025, 3, 20, 10, 0, 0), RawData = "{}" };
    public async Task<GatewayTransactionInfo> InquiryAsync(long t, CancellationToken ct = default)
    {
        InquiryCalls++; if (YieldInInquiry) await Task.Delay(30);
        if (InquiryTransportErrors > 0) { InquiryTransportErrors--; return new GatewayTransactionInfo { TransportError = true }; }
        return Sessions.TryGetValue(t, out var z) ? Info(z, 100) : new GatewayTransactionInfo { ResultCode = 203 };
    }
    public Task<GatewayTransactionInfo> VerifyAsync(long t, CancellationToken ct = default)
    {
        VerifyCalls++;
        if (VerifyTransportErrors > 0) { VerifyTransportErrors--; return Task.FromResult(new GatewayTransactionInfo { TransportError = true }); }
        if (!Sessions.TryGetValue(t, out var z)) return Task.FromResult(new GatewayTransactionInfo { ResultCode = 203 });
        if (z.Status == 2) { z.Status = 1; return Task.FromResult(Info(z, VerifyReturns201 ? 201 : 100)); }
        if (z.Status == 1) return Task.FromResult(Info(z, 201));
        return Task.FromResult(new GatewayTransactionInfo { ResultCode = 202, Status = z.Status });
    }
}

public class FakeClock : TimeProvider
{
    public DateTimeOffset Utc = new(2025, 3, 20, 10, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Utc;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    public void Advance(TimeSpan t) => Utc += t;
}
