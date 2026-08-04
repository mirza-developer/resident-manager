namespace ResidentialComplex.Domain.Entities;

/// <summary>
/// Represents a line item within a bill.
/// </summary>
public class BillItem
{
    public int Id { get; set; }
    public int BillId { get; set; }
    public Bill Bill { get; set; } = null!;
    public int FinancialItemId { get; set; }
    public FinancialItem FinancialItem { get; set; } = null!;
    public decimal Amount { get; set; }
    public string? Description { get; set; }
}
