namespace ResidentialComplex.Domain.Enums;

/// <summary>
/// Financial item period types.
/// </summary>
public enum PeriodType
{
    /// <summary>Applied only once to the next generated monthly bill.</summary>
    Once = 0,
    /// <summary>Applied every month permanently.</summary>
    Permanent = 1,
    /// <summary>Distributed across multiple months as installments.</summary>
    Installment = 2
}
