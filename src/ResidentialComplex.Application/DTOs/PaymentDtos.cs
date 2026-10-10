namespace ResidentialComplex.Application.DTOs;

/// <summary>Data needed to open a payment session at the gateway.</summary>
/// <param name="AmountRial">Amount in Rial.</param>
/// <param name="CallbackUrl">Absolute URL the gateway redirects the user back to.</param>
/// <param name="OrderId">Our unique order id (echoed back by the gateway on verify/inquiry).</param>
/// <param name="Description">Free text shown in gateway reports.</param>
/// <param name="Mobile">Optional 09xxxxxxxxx number so the bank page can offer saved cards.</param>
public sealed record GatewayRequest(long AmountRial, string CallbackUrl, string OrderId, string Description, string? Mobile);

/// <summary>Result of the gateway "request" call.</summary>
public sealed class GatewayRequestResult
{
    public bool Success => !TransportError && ResultCode == 100 && TrackId.HasValue;
    public long? TrackId { get; init; }
    public int ResultCode { get; init; }
    public string? Message { get; init; }

    /// <summary>True when no usable answer was received (timeout, network, bad JSON).</summary>
    public bool TransportError { get; init; }

    /// <summary>Sanitized payload for the audit trail (never contains the merchant id).</summary>
    public string? RawData { get; init; }
}

/// <summary>Result of a gateway "verify" or "inquiry" call.</summary>
public sealed class GatewayTransactionInfo
{
    public int ResultCode { get; init; }
    public string? Message { get; init; }

    /// <summary>Gateway payment status (see <see cref="Helpers.ZibalStatusCodes"/>).</summary>
    public int? Status { get; init; }

    /// <summary>Amount in Rial reported by the gateway.</summary>
    public long? Amount { get; init; }
    public string? OrderId { get; init; }
    public long? RefNumber { get; init; }
    public string? CardNumber { get; init; }
    public DateTime? PaidAt { get; init; }
    public DateTime? VerifiedAt { get; init; }
    public bool TransportError { get; init; }
    public string? RawData { get; init; }
}

/// <summary>What the gateway sent to our callback URL (all values untrusted).</summary>
public sealed record PaymentCallback(long TrackId, string? Success, string? Status, string? OrderId, string? RawQuery);

/// <summary>Result of starting a payment.</summary>
public sealed class StartPaymentResult
{
    public bool Success { get; init; }

    /// <summary>Gateway page the browser must be sent to.</summary>
    public string? RedirectUrl { get; init; }
    public Guid? AttemptPublicId { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>True when an earlier attempt turned out to be paid, so no new payment is needed.</summary>
    public bool AlreadyPaid { get; init; }

    public static StartPaymentResult Fail(string message, bool alreadyPaid = false, Guid? attemptPublicId = null) =>
        new() { Success = false, ErrorMessage = message, AlreadyPaid = alreadyPaid, AttemptPublicId = attemptPublicId };
}

public enum PaymentProcessingOutcome
{
    /// <summary>Paid, verified and the bill is now paid.</summary>
    Succeeded,
    /// <summary>The user has not finished paying (yet).</summary>
    Pending,
    /// <summary>The gateway reports a failed / cancelled payment.</summary>
    Failed,
    /// <summary>Money was verified but could not be applied automatically.</summary>
    NeedsReview,
    /// <summary>The attempt was already finished earlier — nothing was done.</summary>
    AlreadyFinal,
    /// <summary>Another request is processing this attempt right now.</summary>
    InProgress,
    /// <summary>A transient problem (e.g. gateway unreachable); can simply be retried.</summary>
    TemporaryError,
    /// <summary>No attempt matches the trackId.</summary>
    UnknownTrack
}

public sealed record PaymentProcessingResult(PaymentProcessingOutcome Outcome, Guid? AttemptPublicId, string Message);
