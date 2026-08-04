namespace ResidentialComplex.Domain.Entities;

/// <summary>
/// Monthly usage count for a house (used in Grouping calculation).
/// </summary>
public class MonthlyUsage
{
    public int Id { get; set; }
    public int HouseId { get; set; }
    public House House { get; set; } = null!;
    public int Year { get; set; }
    public int Month { get; set; }
    public int UsageCount { get; set; }
}
