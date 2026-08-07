namespace ResidentialComplex.Domain.Enums;

/// <summary>
/// Financial item calculation types.
/// </summary>
public enum CalculationType
{
    /// <summary>Final amount divided equally among active houses.</summary>
    EqualDivision = 0,
    /// <summary>Amount distributed by usage-based grouping.</summary>
    Grouping = 1
}
