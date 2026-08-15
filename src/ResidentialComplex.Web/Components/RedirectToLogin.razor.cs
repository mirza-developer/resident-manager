using Microsoft.AspNetCore.Components;

namespace ResidentialComplex.Web.Components;

public partial class RedirectToLogin : ComponentBase
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    protected override void OnInitialized()
    {
        Navigation.NavigateTo($"Account/Login?returnUrl={Uri.EscapeDataString(Navigation.Uri)}", forceLoad: true);
    }
}
