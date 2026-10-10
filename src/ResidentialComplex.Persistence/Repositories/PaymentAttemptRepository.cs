using Microsoft.EntityFrameworkCore;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Domain.Enums;

namespace ResidentialComplex.Persistence.Repositories;

/// <summary>
/// Payment attempts storage. Reads are no-tracking and writes detach the instance afterwards,
/// so a stale tracked copy can never overwrite the result of an atomic conditional update.
/// </summary>
public class PaymentAttemptRepository : IPaymentAttemptRepository
{
    private readonly ApplicationDbContext _db;
    public PaymentAttemptRepository(ApplicationDbContext db) => _db = db;

    public async Task<PaymentAttempt> AddAsync(PaymentAttempt attempt)
    {
        _db.PaymentAttempts.Add(attempt);
        await _db.SaveChangesAsync();
        _db.Entry(attempt).State = EntityState.Detached;
        return attempt;
    }

    public async Task<PaymentAttempt?> GetByIdAsync(int id) =>
        await _db.PaymentAttempts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);

    public async Task<PaymentAttempt?> GetByPublicIdAsync(Guid publicId)
    {
        var attempt = await _db.PaymentAttempts.AsNoTracking()
            .Include(a => a.Bill).ThenInclude(b => b.House).ThenInclude(h => h.Apartment)
            .Include(a => a.Events)
            .FirstOrDefaultAsync(a => a.PublicId == publicId);
        if (attempt is not null)
            attempt.Events = attempt.Events.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).ToList();
        return attempt;
    }

    public async Task<PaymentAttempt?> GetByTrackIdAsync(long trackId) =>
        await _db.PaymentAttempts.AsNoTracking().FirstOrDefaultAsync(a => a.TrackId == trackId);

    public async Task<List<PaymentAttempt>> GetByBillIdAsync(int billId) =>
        await _db.PaymentAttempts.AsNoTracking().Where(a => a.BillId == billId).OrderBy(a => a.Id).ToListAsync();

    public async Task<List<PaymentAttempt>> GetByHouseIdAsync(int houseId) =>
        await _db.PaymentAttempts.AsNoTracking()
            .Include(a => a.Bill)
            .Where(a => a.Bill.HouseId == houseId)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .ToListAsync();

    public async Task<List<PaymentAttempt>> GetAllAsync() =>
        await _db.PaymentAttempts.AsNoTracking()
            .Include(a => a.Bill).ThenInclude(b => b.House).ThenInclude(h => h.Apartment)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .ToListAsync();

    public async Task<int> CountCreatedSinceAsync(int billId, DateTime since) =>
        await _db.PaymentAttempts.CountAsync(a => a.BillId == billId && a.CreatedAt >= since);

    public async Task UpdateAsync(PaymentAttempt attempt)
    {
        var other = _db.ChangeTracker.Entries<PaymentAttempt>()
            .FirstOrDefault(e => e.Entity.Id == attempt.Id && !ReferenceEquals(e.Entity, attempt));
        if (other is not null) other.State = EntityState.Detached;

        _db.Entry(attempt).State = EntityState.Modified;
        await _db.SaveChangesAsync();
        _db.Entry(attempt).State = EntityState.Detached;
    }

    public async Task<bool> TryClaimAsync(int attemptId, DateTime now, DateTime staleBefore)
    {
        var rows = await _db.PaymentAttempts
            .Where(a => a.Id == attemptId && (
                a.Status == PaymentAttemptStatus.Requested
                || a.Status == PaymentAttemptStatus.CallbackReceived
                || a.Status == PaymentAttemptStatus.Verified
                || (a.Status == PaymentAttemptStatus.Verifying && a.UpdatedAt < staleBefore)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, PaymentAttemptStatus.Verifying)
                .SetProperty(a => a.UpdatedAt, now));
        return rows == 1;
    }

    public async Task<bool> TryReleaseAsync(int attemptId, PaymentAttemptStatus status, string? note, DateTime now)
    {
        var rows = await _db.PaymentAttempts
            .Where(a => a.Id == attemptId && a.Status == PaymentAttemptStatus.Verifying)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, status)
                .SetProperty(a => a.Note, note)
                .SetProperty(a => a.UpdatedAt, now));
        return rows == 1;
    }

    public async Task<bool> TryMarkCallbackReceivedAsync(int attemptId, bool? success, int? status, DateTime now)
    {
        var rows = await _db.PaymentAttempts
            .Where(a => a.Id == attemptId && a.Status == PaymentAttemptStatus.Requested)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, PaymentAttemptStatus.CallbackReceived)
                .SetProperty(a => a.CallbackReceivedAt, (DateTime?)now)
                .SetProperty(a => a.CallbackSuccess, success)
                .SetProperty(a => a.CallbackStatus, status)
                .SetProperty(a => a.UpdatedAt, now));
        return rows == 1;
    }

    public async Task AddEventAsync(PaymentAttemptEvent attemptEvent)
    {
        _db.PaymentAttemptEvents.Add(attemptEvent);
        await _db.SaveChangesAsync();
        _db.Entry(attemptEvent).State = EntityState.Detached;
    }
}
