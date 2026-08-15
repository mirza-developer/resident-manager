using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using ResidentialComplex.Application.Helpers;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Domain.Enums;

namespace ResidentialComplex.Web.Components.Pages.Worker;

[Authorize(Roles = "Administrator,Worker")]
public partial class Usage : ComponentBase
{
    [Inject] private IMonthlyUsageRepository UsageRepo { get; set; } = default!;
    [Inject] private IHouseRepository HouseRepo { get; set; } = default!;
    [Inject] private IFinancialItemRepository FinancialItemRepo { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private int year = PersianCalendarHelper.GetCurrentYear();
    private int month = PersianCalendarHelper.GetCurrentMonth();
    private List<UsageItemViewModel> usageItems = new();
    private bool loaded;
    private bool isLoading;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        isLoading = true;
        try
        {
            var houses = await HouseRepo.GetActiveHousesAsync();
            var activeItems = await FinancialItemRepo.GetActiveAsync();
            var groupingItems = activeItems.Where(item => item.CalculationType == CalculationType.Grouping).ToList();

            usageItems = new List<UsageItemViewModel>();
            foreach (var item in groupingItems)
            {
                var usages = await UsageRepo.GetByFinancialItemMonthYearAsync(item.Id, year, month);
                var rows = houses.Select(house =>
                {
                    var usage = usages.FirstOrDefault(current => current.HouseId == house.Id);
                    return new UsageRow
                    {
                        HouseId = house.Id,
                        FinancialItemId = item.Id,
                        HouseTitle = house.Title,
                        UsageCount = usage?.UsageCount ?? 0,
                        ExistingUsageId = usage?.Id
                    };
                }).ToList();

                usageItems.Add(new UsageItemViewModel
                {
                    FinancialItemId = item.Id,
                    Title = item.Title,
                    Rows = rows
                });
            }

            loaded = true;
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بارگذاری اطلاعات مصرف: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task SaveUsageAsync(UsageRow row)
    {
        isLoading = true;
        try
        {
            MonthlyUsage? existing = null;
            if (row.ExistingUsageId.HasValue)
            {
                existing = await UsageRepo.GetByHouseItemMonthYearAsync(row.HouseId, row.FinancialItemId, year, month);
            }
            else
            {
                existing = await UsageRepo.GetByHouseItemMonthYearAsync(row.HouseId, row.FinancialItemId, year, month);
            }

            if (existing is null)
            {
                var created = await UsageRepo.AddAsync(new MonthlyUsage
                {
                    HouseId = row.HouseId,
                    FinancialItemId = row.FinancialItemId,
                    Year = year,
                    Month = month,
                    UsageCount = row.UsageCount
                });
                row.ExistingUsageId = created.Id;
            }
            else
            {
                existing.UsageCount = row.UsageCount;
                await UsageRepo.UpdateAsync(existing);
                row.ExistingUsageId = existing.Id;
            }

            Snackbar.Add("مقدار مصرف ذخیره شد.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در ذخیره مصرف: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task SaveAllForItemAsync(int financialItemId)
    {
        isLoading = true;
        try
        {
            var usageItem = usageItems.FirstOrDefault(item => item.FinancialItemId == financialItemId);
            if (usageItem is null)
            {
                return;
            }

            foreach (var row in usageItem.Rows)
            {
                MonthlyUsage? existing = await UsageRepo.GetByHouseItemMonthYearAsync(row.HouseId, row.FinancialItemId, year, month);
                if (existing is null)
                {
                    var created = await UsageRepo.AddAsync(new MonthlyUsage
                    {
                        HouseId = row.HouseId,
                        FinancialItemId = row.FinancialItemId,
                        Year = year,
                        Month = month,
                        UsageCount = row.UsageCount
                    });
                    row.ExistingUsageId = created.Id;
                }
                else
                {
                    existing.UsageCount = row.UsageCount;
                    await UsageRepo.UpdateAsync(existing);
                    row.ExistingUsageId = existing.Id;
                }
            }

            Snackbar.Add("همه مقادیر ذخیره شد.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در ذخیره گروهی مصرف: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private static string GetMonthName(int currentMonth) => PersianCalendarHelper.GetMonthName(currentMonth);

    public sealed class UsageItemViewModel
    {
        public int FinancialItemId { get; set; }
        public string Title { get; set; } = string.Empty;
        public List<UsageRow> Rows { get; set; } = new();
    }

    public sealed class UsageRow
    {
        public int HouseId { get; set; }
        public int FinancialItemId { get; set; }
        public string HouseTitle { get; set; } = string.Empty;
        public int UsageCount { get; set; }
        public int? ExistingUsageId { get; set; }
    }
}
