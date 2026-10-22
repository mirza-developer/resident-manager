using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Domain.Enums;

namespace ResidentialComplex.Web.Components.Pages.Admin;

[Authorize(Roles = "Administrator")]
public partial class FinancialItems : ComponentBase
{
    [Inject] private IFinancialItemRepository FinancialItemRepo { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private List<FinancialItem> items = new();
    private FinancialItem editing = new() { IsActive = true };
    private bool showForm;
    private bool isLoading;

    private IEnumerable<FinancialItem> WaterParents => items.Where(i => i.CalculationType == CalculationType.WaterParent);

    protected override async Task OnInitializedAsync()
    {
        await LoadItemsAsync();
    }

    private async Task LoadItemsAsync()
    {
        isLoading = true;
        try
        {
            items = await FinancialItemRepo.GetAllAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بارگذاری آیتم‌های مالی: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void ShowAdd()
    {
        editing = new FinancialItem { IsActive = true };
        showForm = true;
    }

    private void CancelEdit()
    {
        showForm = false;
        editing = new FinancialItem { IsActive = true };
    }

    private async Task EditAsync(int id)
    {
        isLoading = true;
        try
        {
            var item = await FinancialItemRepo.GetByIdAsync(id);
            if (item is null)
            {
                Snackbar.Add("آیتم مالی یافت نشد.", Severity.Warning);
                return;
            }

            editing = new FinancialItem
            {
                Id = item.Id,
                Title = item.Title,
                Description = item.Description,
                PeriodType = item.PeriodType,
                CalculationType = item.CalculationType,
                IsActive = item.IsActive,
                TotalAmount = item.TotalAmount,
                NumberOfInstallments = item.NumberOfInstallments,
                InstallmentsBilled = item.InstallmentsBilled,
                ParentId = item.ParentId,
                WaterKind = item.WaterKind,
                RowVersion = item.RowVersion
            };
            showForm = true;
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بارگذاری آیتم مالی: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task SaveAsync()
    {
        isLoading = true;
        int? savedItemId = null;
        var savedItemTitle = editing.Title;
        var wasGrouping = editing.CalculationType == CalculationType.Grouping;

        var validationError = ValidateWaterRules();
        if (validationError != null)
        {
            Snackbar.Add(validationError, Severity.Warning);
            isLoading = false;
            return;
        }

        try
        {
            if (editing.Id == 0)
            {
                await FinancialItemRepo.AddAsync(editing);
                Snackbar.Add("آیتم مالی با موفقیت ایجاد شد.", Severity.Success);
            }
            else
            {
                await FinancialItemRepo.UpdateAsync(editing);
                Snackbar.Add("آیتم مالی با موفقیت بروزرسانی شد.", Severity.Success);
            }

            if (editing.CalculationType == CalculationType.WaterParent)
            {
                await EnsureSharedChildAsync(editing);
            }

            items = await FinancialItemRepo.GetAllAsync();
            var savedItem = items.FirstOrDefault(x => x.Id == editing.Id) ?? items.OrderByDescending(x => x.Id).FirstOrDefault();
            savedItemId = savedItem?.Id;
            savedItemTitle = savedItem?.Title ?? savedItemTitle;
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در ذخیره آیتم مالی: {ex.Message}", Severity.Error);
            return;
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }

        showForm = false;
        editing = new FinancialItem { IsActive = true };

        // For a Grouping (bracket-priced) item, immediately prompt the admin to define
        // its tiers in the modal so the flow feels like a single continuous step.
        if (wasGrouping && savedItemId.HasValue)
        {
            await OpenTiersDialogAsync(savedItemId.Value, savedItemTitle);
        }
    }

    private string? ValidateWaterRules()
    {
        if (editing.CalculationType == CalculationType.WaterParent)
        {
            editing.ParentId = null;
            editing.WaterKind = WaterKind.None;
            return null;
        }

        if (editing.CalculationType == CalculationType.SharedConsumption)
        {
            editing.WaterKind = WaterKind.None;
            if (!editing.ParentId.HasValue)
                return "آیتم مصرف مشترک باید زیرمجموعه یک آیتم والد آب باشد.";
            return null;
        }

        if (editing.CalculationType == CalculationType.Grouping && editing.ParentId.HasValue)
        {
            if (editing.WaterKind == WaterKind.None)
                return "نوع کنتور (آب گرم یا آب سرد) را انتخاب کنید.";
            var duplicate = items.Any(i => i.Id != editing.Id && i.ParentId == editing.ParentId && i.WaterKind == editing.WaterKind);
            if (duplicate)
                return "برای این آیتم والد، آیتم فرزند از همین نوع کنتور قبلاً تعریف شده است.";
            return null;
        }

        editing.ParentId = null;
        editing.WaterKind = WaterKind.None;
        return null;
    }

    private async Task EnsureSharedChildAsync(FinancialItem parent)
    {
        var all = await FinancialItemRepo.GetAllAsync();
        var parentId = all.FirstOrDefault(i => i.Id == parent.Id)?.Id ?? all.OrderByDescending(i => i.Id).First().Id;
        if (all.Any(i => i.ParentId == parentId && i.CalculationType == CalculationType.SharedConsumption))
            return;

        await FinancialItemRepo.AddAsync(new FinancialItem
        {
            Title = $"{parent.Title} - مصرف مشترک",
            PeriodType = parent.PeriodType,
            CalculationType = CalculationType.SharedConsumption,
            IsActive = parent.IsActive,
            ParentId = parentId
        });
        Snackbar.Add("آیتم مصرف مشترک به‌صورت خودکار ایجاد شد.", Severity.Info);
    }

    private async Task DeleteAsync(int id)
    {
        if (items.Any(i => i.ParentId == id))
        {
            Snackbar.Add("ابتدا آیتم‌های فرزند این آیتم را حذف کنید.", Severity.Warning);
            return;
        }

        var confirmed = await DialogService.ShowMessageBox(
            "تأیید حذف",
            "آیا از حذف این آیتم مالی مطمئن هستید؟",
            yesText: "حذف",
            cancelText: "انصراف");
        if (confirmed != true)
        {
            return;
        }

        isLoading = true;
        try
        {
            await FinancialItemRepo.DeleteAsync(id);
            items = await FinancialItemRepo.GetAllAsync();
            Snackbar.Add("آیتم مالی با موفقیت حذف شد.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در حذف آیتم مالی: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task OpenTiersDialogAsync(int financialItemId, string title)
    {
        var parameters = new DialogParameters
        {
            [nameof(FinancialItemTiersDialog.FinancialItemId)] = financialItemId,
            [nameof(FinancialItemTiersDialog.ItemTitle)] = title
        };
        var options = new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseButton = true };
        await DialogService.ShowAsync<FinancialItemTiersDialog>(string.Empty, parameters, options);
        await LoadItemsAsync();
    }

    private string GetParentLabel(FinancialItem item)
    {
        if (!item.ParentId.HasValue)
            return "-";

        var parentTitle = items.FirstOrDefault(p => p.Id == item.ParentId)?.Title ?? "-";
        var kind = item.WaterKind switch
        {
            WaterKind.Hot => " (\u0622\u0628 \u06AF\u0631\u0645)",
            WaterKind.Cold => " (\u0622\u0628 \u0633\u0631\u062F)",
            _ => string.Empty
        };
        return parentTitle + kind;
    }

    private static string GetPeriodLabel(PeriodType periodType) => periodType switch
    {
        PeriodType.Once => "یکبار",
        PeriodType.Permanent => "دائمی",
        PeriodType.Installment => "اقساط",
        _ => string.Empty
    };

    private static string GetCalcLabel(CalculationType calculationType) => calculationType switch
    {
        CalculationType.EqualDivision => "تقسیم مساوی",
        CalculationType.Grouping => "تعرفه‌ای (بر اساس کل مصرف)",
        CalculationType.WaterParent => "آیتم والد آب (قبض مرجع)",
        CalculationType.SharedConsumption => "مصرف مشترک آب",
        _ => string.Empty
    };
}
