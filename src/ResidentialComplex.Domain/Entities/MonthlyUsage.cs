namespace ResidentialComplex.Domain.Entities;

/// <summary>
/// Monthly usage count for a house per financial item (used in Grouping calculation).
/// Each grouping-type financial item tracks usage separately per house per month.
/// </summary>
public class MonthlyUsage
{
    public int Id { get; set; }
    public int HouseId { get; set; }
    public House House { get; set; } = null!;
    public int FinancialItemId { get; set; }
    public FinancialItem FinancialItem { get; set; } = null!;
    public int Year { get; set; }
    public int Month { get; set; }
    public int UsageCount { get; set; }
}
