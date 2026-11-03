using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace ResidentialComplex.Web.Components.Pages.Admin;

/// <summary>Result of sending the message to one house (shown in <see cref="SendSmsResultDialog"/>).</summary>
public record SmsSendOutcome(int HouseId, string HouseTitle, string ResidentName, string Phone, bool Success, string? Detail);

public partial class SendSmsResultDialog : ComponentBase
{
    [CascadingParameter] private MudDialogInstance MudDialog { get; set; } = default!;

    [Parameter] public List<SmsSendOutcome> Outcomes { get; set; } = new();

    private List<SmsSendOutcome> Succeeded => Outcomes.Where(o => o.Success).ToList();
    private List<SmsSendOutcome> Failed => Outcomes.Where(o => !o.Success).ToList();

    private void Close() => MudDialog.Close();
}
