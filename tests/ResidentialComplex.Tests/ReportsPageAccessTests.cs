using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using ResidentialComplex.Web.Components.Pages.Admin;
using Xunit;

namespace ResidentialComplex.Tests;

/// <summary>The redesign must not change who may open the reports page or where it lives.</summary>
public class ReportsPageAccessTests
{
    [Fact]
    public void ReportsPage_IsStillAdministratorOnly()
    {
        var roles = typeof(Reports).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>()
            .SelectMany(a => (a.Roles ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet();

        Assert.True(roles.SetEquals(new[] { "Administrator" }), $"roles were: {string.Join(",", roles)}");
        Assert.Empty(typeof(Reports).GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
    }

    [Fact]
    public void ReportsPage_IsStillRoutedAtAdminReports()
    {
        var routes = typeof(Reports).GetCustomAttributes(typeof(RouteAttribute), inherit: true).Cast<RouteAttribute>().Select(r => r.Template);

        Assert.Contains("/admin/reports", routes);
    }
}
