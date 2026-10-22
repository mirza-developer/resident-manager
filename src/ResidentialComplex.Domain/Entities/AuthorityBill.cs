namespace ResidentialComplex.Domain.Entities;

/// <summary>
/// The bulk bill received from the authority (e.g. water company) for the whole complex for one
/// month and one <see cref="FinancialItem"/> of type AuthorityBill. It is the input of the old
/// PITS allocation formula.
/// </summary>
public class AuthorityBill
{
    public int Id { get; set; }
    public int FinancialItemId { get; set; }
    public FinancialItem FinancialItem { get; set; } = null!;
    public int Year { get; set; }
    public int Month { get; set; }

    /// <summary>Length of the billed period in days (used to normalise usage to a 30-day month).</summary>
    public int PeriodDays { get; set; } = 30;

    /// <summary>Total consumption billed by the authority.</summary>
    public decimal TotalUse { get; set; }

    /// <summary>Total amount billed by the authority (Rial).</summary>
    public decimal TotalPrice { get; set; }

    /// <summary>Tax on the authority bill. Stored for reference only; not used in allocation (as in the old app).</summary>
    public decimal Tax { get; set; }

    public string? Notes { get; set; }
}
