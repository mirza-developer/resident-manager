namespace ResidentialComplex.Domain.Entities;

/// <summary>
/// Represents a payment record for a bill.
/// </summary>
public class Payment
{
    public int Id { get; set; }
    public int BillId { get; set; }
    public Bill Bill { get; set; } = null!;
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public string? Description { get; set; }
}
