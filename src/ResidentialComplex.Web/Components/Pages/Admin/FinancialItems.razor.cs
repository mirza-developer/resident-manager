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
    private List<TierRow> editingTiers = new();
    private bool showForm;
    private bool isLoading;

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
        editingTiers = new();
        showForm = true;
    }

    private void CancelEdit()
    {
        showForm = false;
        editing = new FinancialItem { IsActive = true };
        editingTiers = new();
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
                RowVersion = item.RowVersion
            };
            editingTiers = item.Tiers
                .OrderBy(x => x.TierOrder)
                .Select(x => new TierRow { Id = x.Id, UpperLimit = x.UpperLimit, RatePerUnit = x.RatePerUnit })
                .ToList();
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

            items = await FinancialItemRepo.GetAllAsync();
            var savedItem = items.FirstOrDefault(x => x.Id == editing.Id) ?? items.OrderByDescending(x => x.Id).FirstOrDefault();
            if (editing.CalculationType != CalculationType.Grouping || savedItem is null)
            {
                showForm = false;
                editing = new FinancialItem { IsActive = true };
                editingTiers = new();
                return;
            }

            editing = new FinancialItem
            {
                Id = savedItem.Id,
                Title = savedItem.Title,
                Description = savedItem.Description,
                PeriodType = savedItem.PeriodType,
                CalculationType = savedItem.CalculationType,
                IsActive = savedItem.IsActive,
                TotalAmount = savedItem.TotalAmount,
                NumberOfInstallments = savedItem.NumberOfInstallments,
                InstallmentsBilled = savedItem.InstallmentsBilled,
                RowVersion = savedItem.RowVersion
            };
            editingTiers = savedItem.Tiers
                .OrderBy(x => x.TierOrder)
                .Select(x => new TierRow { Id = x.Id, UpperLimit = x.UpperLimit, RatePerUnit = x.RatePerUnit })
                .ToList();
            showForm = true;
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در ذخیره آیتم مالی: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task DeleteAsync(int id)
    {
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

    private void AddTierRow()
    {
        editingTiers.Add(new TierRow { RatePerUnit = 0m });
    }

    private void RemoveTier(TierRow tier)
    {
        editingTiers.Remove(tier);
    }

    private async Task SaveTiersAsync()
    {
        if (editing.Id == 0)
        {
            Snackbar.Add("ابتدا آیتم مالی را ذخیره کنید.", Severity.Warning);
            return;
        }

        if (editingTiers.Count == 0)
        {
            Snackbar.Add("حداقل یک تعرفه باید تعریف شود.", Severity.Warning);
            return;
        }

        for (var index = 0; index < editingTiers.Count - 1; index++)
        {
            if (editingTiers[index].UpperLimit is null)
            {
                Snackbar.Add("تنها آخرین تعرفه می‌تواند بدون حد بالایی باشد.", Severity.Error);
                return;
            }
        }

        for (var index = 1; index < editingTiers.Count - 1; index++)
        {
            if (editingTiers[index].UpperLimit <= editingTiers[index - 1].UpperLimit)
            {
                Snackbar.Add("حد بالایی هر تعرفه باید از تعرفه قبلی بزرگ‌تر باشد.", Severity.Error);
                return;
            }
        }

        isLoading = true;
        try
        {
            var existing = await FinancialItemRepo.GetTiersAsync(editing.Id);
            foreach (var tier in existing)
            {
                await FinancialItemRepo.DeleteTierAsync(tier.Id);
            }

            for (var index = 0; index < editingTiers.Count; index++)
            {
                var row = editingTiers[index];
                await FinancialItemRepo.AddTierAsync(new FinancialItemTier
                {
                    FinancialItemId = editing.Id,
                    TierOrder = index + 1,
                    UpperLimit = index < editingTiers.Count - 1 ? row.UpperLimit : null,
                    RatePerUnit = row.RatePerUnit
                });
            }

            items = await FinancialItemRepo.GetAllAsync();
            var updated = items.FirstOrDefault(x => x.Id == editing.Id);
            if (updated is not null)
            {
                editingTiers = updated.Tiers.OrderBy(x => x.TierOrder)
                    .Select(x => new TierRow { Id = x.Id, UpperLimit = x.UpperLimit, RatePerUnit = x.RatePerUnit })
                    .ToList();
            }

            Snackbar.Add("تعرفه‌ها با موفقیت ذخیره شدند.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در ذخیره تعرفه‌ها: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
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
        CalculationType.Grouping => "تعرفه پلکانی (IBT)",
        _ => string.Empty
    };

    public sealed class TierRow
    {
        public int Id { get; set; }
        public int? UpperLimit { get; set; }
        public decimal RatePerUnit { get; set; }
    }
}
