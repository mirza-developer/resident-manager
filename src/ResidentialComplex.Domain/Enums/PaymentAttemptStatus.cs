namespace ResidentialComplex.Domain.Enums;

/// <summary>
/// Lifecycle of one online payment attempt (one Zibal "trackId" session) for a bill.
/// Persisted as an int — do not renumber existing values.
/// </summary>
public enum PaymentAttemptStatus
{
    /// <summary>Row created, gateway "request" call not completed yet.</summary>
    Initiated = 0,

    /// <summary>Gateway returned a trackId; the user was sent to the bank page.</summary>
    Requested = 1,

    /// <summary>The gateway "request" call failed — no payment session exists (final).</summary>
    RequestFailed = 2,

    /// <summary>The user came back to our callback URL; not yet confirmed with the gateway.</summary>
    CallbackReceived = 3,

    /// <summary>Claimed by one worker for inquiry/verify (transient, lease based).</summary>
    Verifying = 4,

    /// <summary>Gateway confirmed the payment; settling the bill in our database (transient).</summary>
    Verified = 5,

    /// <summary>Paid, verified and the bill was marked as paid (final).</summary>
    Succeeded = 6,

    /// <summary>The gateway reports the payment was cancelled / declined / failed (final).</summary>
    Failed = 7,

    /// <summary>
    /// Money was verified at the gateway but could not be applied automatically
    /// (amount/order mismatch, bill no longer payable). An administrator must review (final).
    /// </summary>
    NeedsReview = 8,

    /// <summary>The user never completed the payment within the allowed time (final).</summary>
    Expired = 9
}
