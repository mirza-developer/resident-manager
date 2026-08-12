using Microsoft.AspNetCore.Components;

namespace ResidentialComplex.Web.Components.Pages.Account;

public partial class Logout : ComponentBase
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    protected override void OnInitialized()
    {
        Navigation.NavigateTo("/Account/LogoutPost", forceLoad: true);
    }
}
