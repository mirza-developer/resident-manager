using Microsoft.AspNetCore.Components;

namespace ResidentialComplex.Web.Components.Pages.Shared;

public partial class HelpButton
{
    [Parameter] public string PageKey { get; set; } = string.Empty;

    private bool isOpen;

    private void Toggle() => isOpen = !isOpen;
}
