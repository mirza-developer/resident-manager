using ResidentialComplex.Domain.Enums;

namespace ResidentialComplex.Domain.Entities;

/// <summary>
/// One attempt (one gateway session / trackId) to pay a bill online. Every try is stored,
/// whatever its outcome, so administrators can audit them and residents can see their own.
/// </summary>
public class PaymentAttempt
{
    public int Id { get; set; }

    /// <summary>Unguessable public identifier used in URLs (never expose <see cref="Id"/>).</summary>
    public Guid PublicId { get; set; } = Guid.NewGuid();

    public int BillId { get; set; }
    public Bill Bill { get; set; } = null!;

    /// <summary>Amount requested from the gateway, in Rial (copied from the bill when the attempt is created).</summary>
    public decimal Amount { get; set; }

    /// <summary>Gateway name (currently always "Zibal").</summary>
    public string Gateway { get; set; } = "Zibal";

    /// <summary>Our unique, unguessable order id sent to the gateway and checked again on verify.</summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>Gateway session id (Zibal trackId). Null until the request call succeeds.</summary>
    public long? TrackId { get; set; }

    public PaymentAttemptStatus Status { get; set; } = PaymentAttemptStatus.Initiated;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? RequestedAt { get; set; }
    public DateTime? CallbackReceivedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }

    /// <summary>Payment time reported by the gateway.</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>Zibal "result" code of the request call (100 = ok).</summary>
    public int? RequestResultCode { get; set; }

    /// <summary>"success" flag received on the callback (informational only, never trusted).</summary>
    public bool? CallbackSuccess { get; set; }

    /// <summary>"status" received on the callback (informational only, never trusted).</summary>
    public int? CallbackStatus { get; set; }

    /// <summary>Last payment status reported by Zibal inquiry/verify (see Zibal status table).</summary>
    public int? ZibalStatus { get; set; }

    /// <summary>Bank reference number (refNumber) from the gateway.</summary>
    public long? RefNumber { get; set; }

    /// <summary>Masked card number reported by the gateway.</summary>
    public string? CardNumber { get; set; }

    /// <summary>Amount the gateway says was paid, in Rial.</summary>
    public decimal? PaidAmount { get; set; }

    /// <summary>Human readable explanation of the latest outcome / problem.</summary>
    public string? Note { get; set; }

    /// <summary>Identity user id of the person who started the payment.</summary>
    public string? InitiatedByUserId { get; set; }

    /// <summary>The <see cref="Payment"/> row created when this attempt settled the bill.</summary>
    public int? PaymentId { get; set; }
    public Payment? Payment { get; set; }

    public ICollection<PaymentAttemptEvent> Events { get; set; } = new List<PaymentAttemptEvent>();
}
