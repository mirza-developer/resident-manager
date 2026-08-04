namespace ResidentialComplex.Domain.Entities;

/// <summary>
/// Represents a house (unit) within an apartment.
/// </summary>
public class House
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ResidentName { get; set; } = string.Empty;
    public string ResidentPhoneNumber { get; set; } = string.Empty;
    public int NumberOfResidents { get; set; }
    public decimal CurrentDebt { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];

    public int ApartmentId { get; set; }
    public Apartment Apartment { get; set; } = null!;

    /// <summary>
    /// Optional link to a resident user account.
    /// </summary>
    public string? ApplicationUserId { get; set; }

    public ICollection<Bill> Bills { get; set; } = new List<Bill>();
    public ICollection<MonthlyUsage> MonthlyUsages { get; set; } = new List<MonthlyUsage>();
}
