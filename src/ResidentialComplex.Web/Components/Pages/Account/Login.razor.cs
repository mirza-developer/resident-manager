using Microsoft.AspNetCore.Components;

namespace ResidentialComplex.Web.Components.Pages.Account;

public partial class Login
{
    [SupplyParameterFromQuery(Name = "returnUrl")]
    public string? ReturnUrl { get; set; }

    [SupplyParameterFromQuery(Name = "error")]
    public string? ErrorMessage { get; set; }

    private LoginModel loginModel = new();

#if DEBUG
    protected override async Task OnInitializedAsync()
    {
        loginModel = new()
        {
            UserName = "admin",
            Password = "Admin123"
        };
    }
#endif

    private sealed class LoginModel
    {
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public bool RememberMe { get; set; }
    }
}
