using ResidentialComplex.Domain.Enums;

namespace ResidentialComplex.Domain.Entities;

/// <summary>
/// Append-only trace entry of a <see cref="PaymentAttempt"/>: what we sent / received from
/// the gateway and what we decided. The merchant id is never stored in <see cref="Data"/>.
/// </summary>
public class PaymentAttemptEvent
{
    public int Id { get; set; }
    public int PaymentAttemptId { get; set; }
    public PaymentAttempt PaymentAttempt { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
    public PaymentEventType EventType { get; set; }

    /// <summary>Gateway "result" code, when the event is a gateway call.</summary>
    public int? ResultCode { get; set; }

    /// <summary>Gateway payment status, when known.</summary>
    public int? ZibalStatus { get; set; }

    public string? Message { get; set; }

    /// <summary>Sanitized raw payload (JSON / query string) for dispute resolution.</summary>
    public string? Data { get; set; }
}
