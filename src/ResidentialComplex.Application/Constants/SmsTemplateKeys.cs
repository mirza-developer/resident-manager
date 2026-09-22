namespace ResidentialComplex.Application.Constants;

/// <summary>
/// Stable keys identifying which system event an <see cref="Domain.Entities.SmsTemplate"/>
/// is used for. Used to look up the right template row without depending on its (admin-editable) Title.
/// </summary>
public static class SmsTemplateKeys
{
    /// <summary>Sent to a house's resident phone number when a Draft bill is Approved.</summary>
    public const string BillApproved = "BillApproved";
}
