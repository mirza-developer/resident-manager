using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Domain.Enums;

namespace ResidentialComplex.Application.Interfaces;

/// <summary>
/// Storage for online payment attempts and their audit trail.
/// All entities returned/accepted here are detached copies (no change tracking), so the
/// atomic <c>Try…</c> methods can never be defeated by stale tracked instances.
/// </summary>
public interface IPaymentAttemptRepository
{
    Task<PaymentAttempt> AddAsync(PaymentAttempt attempt);

    /// <summary>Scalar fields only (no navigation properties).</summary>
    Task<PaymentAttempt?> GetByIdAsync(int id);

    /// <summary>With bill, house, apartment and events (ordered by time) — for display.</summary>
    Task<PaymentAttempt?> GetByPublicIdAsync(Guid publicId);

    Task<PaymentAttempt?> GetByTrackIdAsync(long trackId);
    Task<List<PaymentAttempt>> GetByBillIdAsync(int billId);

    /// <summary>Attempts of all bills of a house (bill included), newest first.</summary>
    Task<List<PaymentAttempt>> GetByHouseIdAsync(int houseId);

    /// <summary>All attempts (bill + house included), newest first.</summary>
    Task<List<PaymentAttempt>> GetAllAsync();

    Task<int> CountCreatedSinceAsync(int billId, DateTime since);

    /// <summary>Overwrites the scalar fields of an existing attempt.</summary>
    Task UpdateAsync(PaymentAttempt attempt);

    /// <summary>
    /// Atomically moves an attempt to <see cref="PaymentAttemptStatus.Verifying"/> if it is
    /// Requested / CallbackReceived / Verified, or Verifying with a lease older than
    /// <paramref name="staleBefore"/>. Returns false when someone else owns it or it is final.
    /// </summary>
    Task<bool> TryClaimAsync(int attemptId, DateTime now, DateTime staleBefore);

    /// <summary>Gives a claimed (Verifying) attempt back with the given status/note.</summary>
    Task<bool> TryReleaseAsync(int attemptId, PaymentAttemptStatus status, string? note, DateTime now);

    /// <summary>Requested → CallbackReceived plus callback data; no-op for any other status.</summary>
    Task<bool> TryMarkCallbackReceivedAsync(int attemptId, bool? success, int? status, DateTime now);

    Task AddEventAsync(PaymentAttemptEvent attemptEvent);
}
