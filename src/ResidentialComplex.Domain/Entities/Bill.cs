using ResidentialComplex.Domain.Enums;

namespace ResidentialComplex.Domain.Entities;

/// <summary>
/// Represents a monthly bill for a house.
/// </summary>
public class Bill
{
    public int Id { get; set; }
    public int HouseId { get; set; }
    public House House { get; set; } = null!;
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// The house's outstanding debt carried into this bill when it was generated.
    /// Already included in <see cref="TotalAmount"/>; it is not part of any BillItem.
    /// </summary>
    public decimal PreviousDebt { get; set; }
    public string? Description { get; set; }
    public BillStatus Status { get; set; } = BillStatus.Draft;
    public DateTime CreatedDate { get; set; }
    public DateTime? ApprovedDate { get; set; }
    public DateTime? PaidDate { get; set; }
    public long RowVersion { get; set; }

    public ICollection<BillItem> BillItems { get; set; } = new List<BillItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
