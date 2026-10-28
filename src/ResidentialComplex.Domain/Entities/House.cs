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

    /// <summary>
    /// Manual display order of the house. Every list of houses in the app is sorted ascending by this value
    /// (ties fall back to <see cref="Id"/>). Edited on the houses page; initially derived from the title
    /// by <see cref="DefaultSortOrderFromTitle"/>.
    /// </summary>
    public int SortOrder { get; set; }
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

    private const string UnitWord = "واحد";

    /// <summary>
    /// Initial <see cref="SortOrder"/> for a title: N when the title is "واحد N" (N a non-negative integer of up to
    /// 9 digits, written with Latin, Persian or Arabic-Indic digits, e.g. "واحد 12" or "واحد ۱۲"), otherwise 0.
    /// The same rule is applied to existing rows by the AddHouseSortOrder migration.
    /// </summary>
    public static int DefaultSortOrderFromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return 0;

        var text = title.Trim();
        if (!text.StartsWith(UnitWord, StringComparison.Ordinal)) return 0;

        var rest = text[UnitWord.Length..];
        if (rest.Length == 0 || !char.IsWhiteSpace(rest[0])) return 0;

        var digits = new System.Text.StringBuilder(rest.Length);
        foreach (var c in rest.Trim())
        {
            if (c >= '0' && c <= '9') digits.Append(c);
            else if (c >= '\u06F0' && c <= '\u06F9') digits.Append((char)('0' + (c - '\u06F0')));
            else if (c >= '\u0660' && c <= '\u0669') digits.Append((char)('0' + (c - '\u0660')));
            else return 0;
        }

        return digits.Length is >= 1 and <= 9 ? int.Parse(digits.ToString()) : 0;
    }
}
