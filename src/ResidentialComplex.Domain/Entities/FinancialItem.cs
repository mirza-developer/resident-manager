using System.ComponentModel.DataAnnotations;
using ResidentialComplex.Domain.Enums;

namespace ResidentialComplex.Domain.Entities;

/// <summary>
/// Represents a financial item used for billing calculations.
/// </summary>
public class FinancialItem
{
    public int Id { get; set; }

    [Required(ErrorMessage = "عنوان الزامی است.")]
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PeriodType PeriodType { get; set; }
    public CalculationType CalculationType { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Total amount for installment period type.</summary>
    public decimal? TotalAmount { get; set; }
    /// <summary>Number of installments for installment period type.</summary>
    public int? NumberOfInstallments { get; set; }
    /// <summary>Number of installments already billed.</summary>
    public int InstallmentsBilled { get; set; }

    public long RowVersion { get; set; }

    /// <summary>Parent item (e.g. water) for child items such as hot, cold and shared consumption.</summary>
    public int? ParentId { get; set; }
    public FinancialItem? Parent { get; set; }
    /// <summary>For Grouping children of a WaterParent: whether this is the hot or the cold meter item.</summary>
    public WaterKind WaterKind { get; set; }
    public ICollection<FinancialItem> Children { get; set; } = new List<FinancialItem>();

    public ICollection<FinancialItemGroupPoint> GroupPoints { get; set; } = new List<FinancialItemGroupPoint>();
    /// <summary>IBT tiers for Grouping calculation type, ordered by TierOrder.</summary>
    public ICollection<FinancialItemTier> Tiers { get; set; } = new List<FinancialItemTier>();
    public ICollection<BillItem> BillItems { get; set; } = new List<BillItem>();
}
