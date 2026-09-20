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
                var rows = new List<UsageRow>();
                foreach (var house in houses)
                {
                    rows.Add(await BuildRowAsync(house.Id, house.Title, item.Id));
                }

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

    /// <summary>
    /// Builds a usage row for a house/item in the selected month. If a record for this
    /// exact month already exists, its stored readings are used (previous reading locked,
    /// since changing it retroactively would break the chain with adjacent months). Otherwise,
    /// the previous month's CurrentReading (if any) is carried forward as this month's
    /// PreviousReading and locked; only if no prior record exists at all is PreviousReading
    /// left editable, for the very first reading ever taken.
    /// </summary>
    private async Task<UsageRow> BuildRowAsync(int houseId, string houseTitle, int financialItemId)
    {
        var existing = await UsageRepo.GetByHouseItemMonthYearAsync(houseId, financialItemId, year, month);
        if (existing is not null)
        {
            return new UsageRow
            {
                HouseId = houseId,
                FinancialItemId = financialItemId,
                HouseTitle = houseTitle,
                PreviousReading = existing.PreviousReading,
                CurrentReading = existing.CurrentReading,
                UsageCount = existing.UsageCount,
                IsPreviousReadingLocked = true,
                ExistingUsageId = existing.Id
            };
        }

        var priorRecord = await UsageRepo.GetLatestBeforeAsync(houseId, financialItemId, year, month);
        var previousReading = priorRecord?.CurrentReading ?? 0;

        var row = new UsageRow
        {
            HouseId = houseId,
            FinancialItemId = financialItemId,
            HouseTitle = houseTitle,
            PreviousReading = previousReading,
            CurrentReading = previousReading,
            IsPreviousReadingLocked = priorRecord is not null,
            ExistingUsageId = null
        };
        RecalculateUsage(row);
        return row;
    }

    private void OnPreviousReadingChanged(UsageRow row, int value)
    {
        row.PreviousReading = value;
        RecalculateUsage(row);
    }

    private void OnCurrentReadingChanged(UsageRow row, int value)
    {
        row.CurrentReading = value;
        RecalculateUsage(row);
    }

    private static void RecalculateUsage(UsageRow row)
    {
        row.UsageCount = Math.Max(0, row.CurrentReading - row.PreviousReading);
    }

    private bool TryValidate(UsageRow row)
    {
        if (row.CurrentReading < row.PreviousReading)
        {
            Snackbar.Add($"کنتور فعلی واحد «{row.HouseTitle}» نمی‌تواند کمتر از کنتور قبلی باشد.", Severity.Warning);
            return false;
        }

        return true;
    }

    private async Task SaveUsageAsync(UsageRow row)
    {
        if (!TryValidate(row))
        {
            return;
        }

        isLoading = true;
        try
        {
            await PersistRowAsync(row);
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

            var invalidHouses = new List<string>();
            foreach (var row in usageItem.Rows)
            {
                if (!TryValidate(row))
                {
                    invalidHouses.Add(row.HouseTitle);
                    continue;
                }

                await PersistRowAsync(row);
            }

            if (invalidHouses.Count > 0)
            {
                Snackbar.Add($"سایر مقادیر ذخیره شد، اما واحدهای زیر رد شدند (کنتور فعلی کمتر از قبلی): {string.Join("، ", invalidHouses)}", Severity.Warning);
            }
            else
            {
                Snackbar.Add("همه مقادیر ذخیره شد.", Severity.Success);
            }
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

    private async Task PersistRowAsync(UsageRow row)
    {
        RecalculateUsage(row);

        var existing = await UsageRepo.GetByHouseItemMonthYearAsync(row.HouseId, row.FinancialItemId, year, month);
        if (existing is null)
        {
            var created = await UsageRepo.AddAsync(new MonthlyUsage
            {
                HouseId = row.HouseId,
                FinancialItemId = row.FinancialItemId,
                Year = year,
                Month = month,
                PreviousReading = row.PreviousReading,
                CurrentReading = row.CurrentReading,
                UsageCount = row.UsageCount
            });
            row.ExistingUsageId = created.Id;
        }
        else
        {
            existing.PreviousReading = row.PreviousReading;
            existing.CurrentReading = row.CurrentReading;
            existing.UsageCount = row.UsageCount;
            await UsageRepo.UpdateAsync(existing);
            row.ExistingUsageId = existing.Id;
        }

        row.IsPreviousReadingLocked = true;
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

        /// <summary>Meter reading at the start of the month (lower limit).</summary>
        public int PreviousReading { get; set; }

        /// <summary>Meter reading at the end of the month (upper limit).</summary>
        public int CurrentReading { get; set; }

        /// <summary>Computed: CurrentReading - PreviousReading. Read-only in the UI.</summary>
        public int UsageCount { get; set; }

        /// <summary>
        /// True once a previous reading is established (either loaded from the current
        /// month's own saved record, or carried forward from the previous month), so the
        /// field is shown read-only and the reading chain can't be broken by accident.
        /// False only for the very first reading ever taken for this house/item.
        /// </summary>
        public bool IsPreviousReadingLocked { get; set; }

        public int? ExistingUsageId { get; set; }
    }
}
