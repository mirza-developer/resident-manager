namespace ResidentialComplex.Domain.Enums;

/// <summary>Kinds of entries in a payment attempt's audit trail.</summary>
public enum PaymentEventType
{
    Created = 0,
    GatewayRequest = 1,
    Callback = 2,
    Inquiry = 3,
    Verify = 4,
    Settled = 5,
    StatusChanged = 6,
    Warning = 7
}
