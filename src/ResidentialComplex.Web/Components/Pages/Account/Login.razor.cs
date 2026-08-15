using Microsoft.AspNetCore.Components;

namespace ResidentialComplex.Web.Components.Pages.Account;

public partial class Login
{
    [SupplyParameterFromQuery(Name = "returnUrl")]
    public string? ReturnUrl { get; set; }

    [SupplyParameterFromQuery(Name = "error")]
    public string? ErrorMessage { get; set; }

    private LoginModel loginModel = new();

    private sealed class LoginModel
    {
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public bool RememberMe { get; set; }
    }
}
