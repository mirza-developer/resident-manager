using ResidentialComplex.Application.Constants;

namespace ResidentialComplex.Application.Helpers;

/// <summary>
/// Renders an SMS template by replacing "{Token}" placeholders with actual values, and
/// documents which placeholders are available for each template key so the admin UI and
/// BillingService (or any future sender) stay in sync.
/// </summary>
public static class SmsTemplateRenderer
{
    /// <summary>
    /// Replaces every "{Key}" occurrence in <paramref name="template"/> with the matching
    /// value from <paramref name="values"/>. Tokens with no matching key are left as-is
    /// (rather than throwing), so a typo in the template doesn't break sending.
    /// </summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        var result = template;
        foreach (var pair in values)
        {
            result = result.Replace("{" + pair.Key + "}", pair.Value, StringComparison.Ordinal);
        }

        return result;
    }

    /// <summary>
    /// Placeholders available for the <see cref="SmsTemplateKeys.BillApproved"/> template,
    /// with a Persian description shown to the admin as a reference while editing the text.
    /// Keep this in sync with the token dictionary built in BillingService.ApproveBillsAsync.
    /// </summary>
    public static readonly IReadOnlyList<(string Token, string Description)> BillApprovedPlaceholders = new List<(string, string)>
    {
        ("{ResidentName}", "نام مالک/ساکن واحد"),
        ("{HouseTitle}", "عنوان واحد"),
        ("{ApartmentTitle}", "عنوان مجتمع/آپارتمان"),
        ("{PeriodTitle}", "عنوان دوره صورتحساب (مثلاً فروردین ۱۴۰۴)"),
        ("{TotalAmount}", "مبلغ قابل پرداخت این قبض (تومان)"),
        ("{CurrentDebt}", "بدهی فعلی واحد پس از این قبض (تومان)"),
    };

    /// <summary>
    /// Placeholders for a given template key, or an empty list if the key has none registered.
    /// Lets the admin UI show the right reference list for whichever template is being edited.
    /// </summary>
    public static IReadOnlyList<(string Token, string Description)> GetPlaceholders(string key) => key switch
    {
        SmsTemplateKeys.BillApproved => BillApprovedPlaceholders,
        _ => Array.Empty<(string, string)>()
    };
}
