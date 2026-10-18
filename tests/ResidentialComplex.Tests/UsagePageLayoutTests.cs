using System.Text.RegularExpressions;
using Xunit;

namespace ResidentialComplex.Tests;

/// <summary>
/// Regression guards for the worker "monthly usage" page (/worker/usage). The page used to stack one full-size grid per
/// financial item, so its height grew without limit. These tests fail if that layout comes back.
/// </summary>
public class UsagePageLayoutTests
{
    private static string Read(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ResidentialComplex.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "src", "ResidentialComplex.Web", "Components", "Pages", "Worker" }.Concat(relativeParts).ToArray()));
    }

    [Fact]
    public void EveryGrid_HasAFixedHeight_WithAStickyHeader()
    {
        var razor = Read("Usage.razor");

        var grids = Regex.Matches(razor, @"<MudDataGrid\s[^>]*>", RegexOptions.Singleline).Select(m => m.Value).ToList();

        Assert.NotEmpty(grids);
        Assert.All(grids, grid =>
        {
            Assert.Contains("Height=", grid);                 // rows scroll inside the grid, the page does not grow
            Assert.Contains("FixedHeader=\"true\"", grid);    // header stays visible while scrolling
        });
    }

    [Fact]
    public void FinancialItems_AreTabs_NotStackedGrids()
    {
        var razor = Read("Usage.razor");

        var tabs = razor.IndexOf("<MudTabs", StringComparison.Ordinal);
        var loop = razor.IndexOf("@foreach (var usageItem in usageItems)", StringComparison.Ordinal);
        Assert.True(tabs >= 0, "the page must use MudTabs");
        Assert.True(loop > tabs, "the per-item loop must be inside the tabs, so only one grid is rendered at a time");
        Assert.Contains("<MudTabPanel", razor);
    }

    [Fact]
    public void Grid_OffersSearch_AndPageSizeChoices()
    {
        var razor = Read("Usage.razor");

        Assert.Contains("QuickFilter=", razor);
        Assert.Contains("PageSizeOptions=", razor);
    }

    [Fact]
    public void LoadingABusyMonth_DoesNotQueryEachHouseSeparately()
    {
        var code = Read("Usage.razor.cs");

        // existing readings of an item come from ONE query; BuildRowAsync only handles the carry-forward lookup
        Assert.Contains("GetByFinancialItemMonthYearAsync", code);
        var build = code[code.IndexOf("private async Task<UsageRow> BuildRowAsync", StringComparison.Ordinal)..];
        build = build[..build.IndexOf("private void OnPreviousReadingChanged", StringComparison.Ordinal)];
        Assert.DoesNotContain("GetByHouseItemMonthYearAsync", build);
    }
}
