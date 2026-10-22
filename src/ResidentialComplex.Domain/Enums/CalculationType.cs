namespace ResidentialComplex.Domain.Enums;

/// <summary>
/// Financial item calculation types.
/// </summary>
public enum CalculationType
{
    /// <summary>Final amount divided equally among active houses.</summary>
    EqualDivision = 0,
    /// <summary>
    /// Parent-child tiering. An item without a ParentId is a parent that owns the authority bill
    /// and is never billed itself. An item with a ParentId is a child (hot, cold or shared, see
    /// WaterKind) billed through the parent's authority bill using the old PITS allocation formula.
    /// </summary>
    ParentChildTiering = 1
}
