using ResidentialComplex.Application.DTOs;

namespace ResidentialComplex.Application.Interfaces;

/// <summary>
/// Abstraction over an online payment gateway (implemented for Zibal in Infrastructure).
/// Implementations must never throw for gateway/network problems — they return a result with
/// <c>TransportError = true</c> instead — and must never leak the merchant id in <c>RawData</c>.
/// </summary>
public interface IPaymentGateway
{
    string Name { get; }

    /// <summary>False when the gateway credentials are missing (feature disabled).</summary>
    bool IsConfigured { get; }

    Task<GatewayRequestResult> RequestPaymentAsync(GatewayRequest request, CancellationToken ct = default);

    /// <summary>Read-only status of a session (does not change anything at the gateway).</summary>
    Task<GatewayTransactionInfo> InquiryAsync(long trackId, CancellationToken ct = default);

    /// <summary>Confirms a paid session. Must be called by us to finish the payment.</summary>
    Task<GatewayTransactionInfo> VerifyAsync(long trackId, CancellationToken ct = default);

    /// <summary>URL the user's browser must be redirected to in order to pay.</summary>
    string GetStartUrl(long trackId);
}
