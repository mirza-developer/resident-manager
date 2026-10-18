using System.Text.RegularExpressions;
using Xunit;

namespace ResidentialComplex.Tests;

/// <summary>Guards for the admin reports page (/admin/reports): tabs, charts in the first view, no ever-growing page.</summary>
public class ReportsPageLayoutTests
{
    private static string Read(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ResidentialComplex.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "ResidentialComplex.Web", "Components", "Pages", "Admin", file));
    }

    [Fact]
    public void SectionsAreTabs_WithTheOverviewFirst()
    {
        var razor = Read("Reports.razor");

        var tabs = razor.IndexOf("<MudTabs", StringComparison.Ordinal);
        var panels = Regex.Matches(razor, @"<MudTabPanel\s").Count;
        Assert.True(tabs >= 0, "the page must use MudTabs");
        Assert.True(panels >= 4, $"expected overview + 3 detail tabs, found {panels}");

        var firstPanel = razor.IndexOf("<MudTabPanel", StringComparison.Ordinal);
        Assert.Contains("نمای کلی", razor.Substring(firstPanel, 80));
    }

    [Fact]
    public void TheFirstTab_ShowsTheCharts()
    {
        var razor = Read("Reports.razor");

        var first = razor.IndexOf("<MudTabPanel", StringComparison.Ordinal);
        var second = razor.IndexOf("<MudTabPanel", first + 1, StringComparison.Ordinal);
        var firstTab = razor.Substring(first, second - first);

        Assert.True(Regex.Matches(firstTab, @"<MudChart\s").Count >= 4, "overview needs the monthly, status, apartment and house charts");
        Assert.Contains("ChartType.Donut", firstTab);
        Assert.Contains("ChartType.Bar", firstTab);
        Assert.DoesNotContain("<MudDataGrid", firstTab);   // grids live in their own tabs
    }

    [Fact]
    public void EveryGrid_HasAFixedHeight_WithAStickyHeader()
    {
        var razor = Read("Reports.razor");

        var grids = Regex.Matches(razor, @"<MudDataGrid\s[^>]*>", RegexOptions.Singleline).Select(m => m.Value).ToList();

        Assert.True(grids.Count >= 3);
        Assert.All(grids, grid =>
        {
            Assert.Contains("Height=", grid);
            Assert.Contains("FixedHeader=\"true\"", grid);
        });
    }

    [Fact]
    public void TheReportLoadsAutomatically_SoTheChartsAreVisibleOnFirstView()
    {
        var code = Read("Reports.razor.cs");

        var init = code[code.IndexOf("protected override async Task OnInitializedAsync", StringComparison.Ordinal)..];
        init = init[..init.IndexOf("private async Task LoadFiltersAsync", StringComparison.Ordinal)];
        Assert.Contains("LoadReportAsync", init);
        Assert.Contains("ReportChartData.Build", code);   // chart numbers come from the tested helper, not ad-hoc math in the page
    }

    [Fact]
    public void TheFiltersAreStillThere()
    {
        var razor = Read("Reports.razor");

        Assert.Contains("filterYear", razor);
        Assert.Contains("filterMonth", razor);
        Assert.Contains("filterHouseId", razor);
        Assert.Contains("LoadReportAsync", razor);
    }
}
