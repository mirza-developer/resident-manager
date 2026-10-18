using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using ResidentialComplex.Web.Components.Pages.Worker;
using Xunit;

namespace ResidentialComplex.Tests;

/// <summary>The redesign must not change who may open the usage page or where it lives.</summary>
public class UsagePageAccessTests
{
    [Fact]
    public void UsagePage_IsStillOnlyForAdministratorAndWorker()
    {
        var roles = typeof(Usage).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>()
            .SelectMany(a => (a.Roles ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet();

        Assert.True(roles.SetEquals(new[] { "Administrator", "Worker" }), $"roles were: {string.Join(",", roles)}");
        Assert.Empty(typeof(Usage).GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
    }

    [Fact]
    public void UsagePage_IsStillRoutedAtWorkerUsage()
    {
        var routes = typeof(Usage).GetCustomAttributes(typeof(RouteAttribute), inherit: true).Cast<RouteAttribute>().Select(r => r.Template);

        Assert.Contains("/worker/usage", routes);
    }
}
