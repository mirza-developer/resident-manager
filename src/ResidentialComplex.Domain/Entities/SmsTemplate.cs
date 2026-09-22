namespace ResidentialComplex.Domain.Entities;

/// <summary>
/// A configurable, admin-editable SMS message template. The <see cref="Text"/> may contain
/// placeholder tokens such as {ResidentName} or {TotalAmount}, which are substituted with
/// actual house/bill data at send time — see SmsTemplateRenderer in the Application layer.
/// </summary>
public class SmsTemplate
{
    public int Id { get; set; }

    /// <summary>
    /// Stable identifier for which system event this template is used for (e.g. "BillApproved").
    /// Matched in code via SmsTemplateKeys — not shown to the admin, who only sees <see cref="Title"/>.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Human-readable name shown to the admin in the SMS templates page (e.g. "پیامک صدور قبض").</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The message body, which may contain placeholder tokens such as {ResidentName}, {TotalAmount}, etc.</summary>
    public string Text { get; set; } = string.Empty;

    public DateTime? UpdatedAtUtc { get; set; }
    public long RowVersion { get; set; }
}
