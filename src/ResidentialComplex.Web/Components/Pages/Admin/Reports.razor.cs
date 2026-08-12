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
    private List<House> houses = new();
    private bool isLoading;

    protected override async Task OnInitializedAsync()
    {
        await LoadFiltersAsync();
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

    private static string GetMonthName(int month) => PersianCalendarHelper.GetMonthName(month);
}
