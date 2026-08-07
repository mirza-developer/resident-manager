namespace ResidentialComplex.Domain.Entities;

/// <summary>
/// Represents a residential apartment (block/building).
/// </summary>
public class Apartment
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long RowVersion { get; set; }

    public ICollection<House> Houses { get; set; } = new List<House>();
}
