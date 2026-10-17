using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using ResidentialComplex.Web.Components.Pages.Account;
using Xunit;

namespace ResidentialComplex.Tests;

/// <summary>Authorization and routing of the "My account" page (checked on the compiled component).</summary>
public class AccountPageAccessTests
{
    [Fact]
    public void ProfilePage_RequiresLogin_AndIsOpenToEveryRole()
    {
        var authorize = typeof(Profile).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>().ToList();
        var roles = authorize
            .SelectMany(a => (a.Roles ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet();

        Assert.NotEmpty(authorize);
        Assert.True(roles.SetEquals(new[] { "Administrator", "Worker", "Resident" }), $"roles were: {string.Join(",", roles)}");
        Assert.Empty(typeof(Profile).GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
    }

    [Fact]
    public void ProfilePage_IsRoutedAtAccountProfile()
    {
        var routes = typeof(Profile).GetCustomAttributes(typeof(RouteAttribute), inherit: true).Cast<RouteAttribute>().Select(r => r.Template);

        Assert.Contains("/Account/Profile", routes);
    }
}
