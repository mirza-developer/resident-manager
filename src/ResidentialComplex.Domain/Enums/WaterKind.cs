namespace ResidentialComplex.Domain.Enums;

/// <summary>
/// Identifies which metered water child (of a WaterParent item) a Grouping item represents.
/// </summary>
public enum WaterKind
{
    None = 0,
    Hot = 1,
    Cold = 2,
    Shared = 3
}
