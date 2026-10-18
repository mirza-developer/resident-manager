using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using ResidentialComplex.Application.DTOs;
using ResidentialComplex.Application.Helpers;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Application.Services;
using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Web.Components.Pages.Admin;

[Authorize(Roles = "Administrator")]
public partial class Reports : ComponentBase
{
    [Inject] private ReportService ReportService { get; set; } = default!;
    [Inject] private IHouseRepository HouseRepo { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private int filterYear = PersianCalendarHelper.GetCurrentYear();
    private int filterMonth;
    private int filterHouseId;
    private FinancialReportDto? report;
    private ReportChartData charts = ReportChartData.Build(new FinancialReportDto());
    private List<ChartSeries> monthlySeries = new();
    private List<ChartSeries> apartmentSeries = new();
    private List<ChartSeries> houseSeries = new();

    // Grids scroll inside this height (sticky header), so a tab never makes the page grow with the number of rows.
    private const string GridHeight = "max(320px, calc(100vh - 420px))";
    private static readonly int[] pageSizeOptions = { 10, 25, 50, 100 };
    private List<House> houses = new();
    private bool isLoading;

    protected override async Task OnInitializedAsync()
    {
        await LoadFiltersAsync();
        // the overview (KPIs + charts) is the first view, so it is shown right away for the current year
        await LoadReportAsync();
    }

    private async Task LoadFiltersAsync()
    {
        isLoading = true;
        try
        {
            houses = await HouseRepo.GetAllAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بارگذاری واحدها: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadReportAsync()
    {
        isLoading = true;
        try
        {
            report = await ReportService.GenerateReportAsync(
                filterYear > 0 ? filterYear : null,
                filterMonth > 0 ? filterMonth : null,
                filterHouseId > 0 ? filterHouseId : null);
            BuildCharts();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در تولید گزارش: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void BuildCharts()
    {
        charts = ReportChartData.Build(report ?? new FinancialReportDto());
        monthlySeries = new List<ChartSeries>
        {
            new() { Name = "صورتحساب", Data = charts.BilledByMonth },
            new() { Name = "پرداخت شده", Data = charts.PaidByMonth }
        };
        apartmentSeries = new List<ChartSeries> { new() { Name = "بدهی", Data = charts.TopApartmentDebts } };
        houseSeries = new List<ChartSeries> { new() { Name = "بدهی", Data = charts.TopHouseDebts } };
    }

    private static string GetMonthName(int month) => PersianCalendarHelper.GetMonthName(month);
    private static string GetMonthFilterName(int month) => month == 0 ? "همه" : PersianCalendarHelper.GetMonthName(month);
}
