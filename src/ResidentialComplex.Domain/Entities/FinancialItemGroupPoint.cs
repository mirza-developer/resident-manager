namespace ResidentialComplex.Domain.Entities;

/// <summary>
/// Point value for a group in a Grouping-type financial item.
/// </summary>
public class FinancialItemGroupPoint
{
    public int Id { get; set; }
    public int FinancialItemId { get; set; }
    public FinancialItem FinancialItem { get; set; } = null!;

    /// <summary>Group number (1-based).</summary>
    public int GroupNumber { get; set; }
    /// <summary>Point value assigned to this group.</summary>
    public decimal PointValue { get; set; }
}
