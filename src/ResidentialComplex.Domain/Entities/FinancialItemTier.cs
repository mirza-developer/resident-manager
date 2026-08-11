namespace ResidentialComplex.Domain.Entities;

/// <summary>
/// Defines a tier in the Increasing Block Tariff (IBT) for a Grouping-type financial item.
/// Tiers are ordered by <see cref="TierOrder"/>. The last tier has a null <see cref="UpperLimit"/> (unbounded).
/// </summary>
public class FinancialItemTier
{
    public int Id { get; set; }
    public int FinancialItemId { get; set; }
    public FinancialItem FinancialItem { get; set; } = null!;

    /// <summary>1-based order of this tier (lowest usage first).</summary>
    public int TierOrder { get; set; }

    /// <summary>
    /// Inclusive upper bound of usage units for this tier.
    /// Null means this is the last (unbounded) tier.
    /// </summary>
    public int? UpperLimit { get; set; }

    /// <summary>Rate charged per unit of usage that falls within this tier.</summary>
    public decimal RatePerUnit { get; set; }
}
