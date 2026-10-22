namespace ResidentialComplex.Domain.Enums;

/// <summary>
/// Financial item calculation types.
/// </summary>
public enum CalculationType
{
    /// <summary>Final amount divided equally among active houses.</summary>
    EqualDivision = 0,
    /// <summary>Amount distributed by usage-based grouping.</summary>
    Grouping = 1,
    /// <summary>
    /// Parent item (e.g. water) that owns the authority bill. It is never billed itself; its
    /// hot/cold (Grouping) children and its SharedConsumption child are billed through the
    /// old PITS allocation formula.
    /// </summary>
    WaterParent = 2,
    /// <summary>
    /// Child of a WaterParent: unmetered (shared) consumption, split equally among houses
    /// from the parent's authority bill. No manual amount or usage entry.
    /// </summary>
    SharedConsumption = 3
}
