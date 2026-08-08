using System.ComponentModel.DataAnnotations;

namespace ResidentialComplex.Domain.Entities;

/// <summary>
/// Represents a house (unit) within an apartment.
/// </summary>
public class House
{
    public int Id { get; set; }

    [Required(ErrorMessage = "عنوان الزامی است.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "نام ساکن الزامی است.")]
    public string ResidentName { get; set; } = string.Empty;

    [Required(ErrorMessage = "شماره تلفن ساکن الزامی است.")]
    public string ResidentPhoneNumber { get; set; } = string.Empty;

    [Range(0, int.MaxValue, ErrorMessage = "تعداد ساکنین باید مقدار مثبت باشد.")]
    public int NumberOfResidents { get; set; }

    public decimal CurrentDebt { get; set; }
    public bool IsActive { get; set; } = true;
    public long RowVersion { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "انتخاب آپارتمان الزامی است.")]
    public int ApartmentId { get; set; }
    public Apartment Apartment { get; set; } = null!;

    /// <summary>
    /// Optional link to a resident user account.
    /// </summary>
    public string? ApplicationUserId { get; set; }

    public ICollection<Bill> Bills { get; set; } = new List<Bill>();
    public ICollection<MonthlyUsage> MonthlyUsages { get; set; } = new List<MonthlyUsage>();
}
