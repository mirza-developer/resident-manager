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

    /// <summary>Number of groups for Grouping calculation type.</summary>
    public int? NumberOfGroups { get; set; }

    public long RowVersion { get; set; }

    public ICollection<FinancialItemGroupPoint> GroupPoints { get; set; } = new List<FinancialItemGroupPoint>();
    public ICollection<BillItem> BillItems { get; set; } = new List<BillItem>();
}
