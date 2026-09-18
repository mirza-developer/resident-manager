using Microsoft.AspNetCore.Components;
using MudBlazor;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Web.Components.Pages.Admin;

public partial class FinancialItemTiersDialog : ComponentBase
{
    [CascadingParameter] private MudDialogInstance MudDialog { get; set; } = default!;
    [Inject] private IFinancialItemRepository FinancialItemRepo { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    [Parameter] public int FinancialItemId { get; set; }
    [Parameter] public string ItemTitle { get; set; } = string.Empty;

    private List<TierRow> rows = new();
    private bool isLoading;
    private bool isSaving;

    protected override async Task OnInitializedAsync()
    {
        await LoadTiersAsync();
    }

    private async Task LoadTiersAsync()
    {
        isLoading = true;
        try
        {
            var tiers = await FinancialItemRepo.GetTiersAsync(FinancialItemId);
            rows = tiers
                .OrderBy(x => x.TierOrder)
                .Select(x => new TierRow { Id = x.Id, UpperLimit = x.UpperLimit, RatePerUnit = x.RatePerUnit })
                .ToList();

            if (rows.Count == 0)
            {
                rows.Add(new TierRow());
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بارگذاری تعرفه‌ها: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
        }
    }

    private void AddRow()
    {
        rows.Add(new TierRow());
    }

    private void RemoveRow(TierRow row)
    {
        if (rows.Count <= 1)
        {
            Snackbar.Add("حداقل یک تعرفه باید باقی بماند.", Severity.Warning);
            return;
        }

        rows.Remove(row);
    }

    private async Task SaveAsync()
    {
        if (rows.Count == 0)
        {
            Snackbar.Add("حداقل یک تعرفه باید تعریف شود.", Severity.Warning);
            return;
        }

        // Tiers can be entered in any order — there is no manual ordering rule for the
        // user to satisfy. We sort by consumption limit automatically before saving.
        // A row left blank (no upper limit) is treated as the open-ended top tier and
        // naturally sorts last. Ties keep their original entry order.
        var sortedRows = rows
            .Select((row, originalIndex) => (row, originalIndex))
            .OrderBy(entry => entry.row.UpperLimit ?? int.MaxValue)
            .ThenBy(entry => entry.originalIndex)
            .Select(entry => entry.row)
            .ToList();

        isSaving = true;
        try
        {
            var existingTiers = await FinancialItemRepo.GetTiersAsync(FinancialItemId);
            foreach (var tier in existingTiers)
            {
                await FinancialItemRepo.DeleteTierAsync(tier.Id);
            }

            for (var index = 0; index < sortedRows.Count; index++)
            {
                var row = sortedRows[index];
                await FinancialItemRepo.AddTierAsync(new FinancialItemTier
                {
                    FinancialItemId = FinancialItemId,
                    TierOrder = index + 1,
                    UpperLimit = row.UpperLimit,
                    RatePerUnit = row.RatePerUnit
                });
            }

            Snackbar.Add("تعرفه‌ها با موفقیت ذخیره شدند.", Severity.Success);
            MudDialog.Close(DialogResult.Ok(true));
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در ذخیره تعرفه‌ها: {ex.Message}", Severity.Error);
        }
        finally
        {
            isSaving = false;
        }
    }

    private void Cancel() => MudDialog.Cancel();

    public sealed class TierRow
    {
        public int Id { get; set; }
        public int? UpperLimit { get; set; }
        public decimal RatePerUnit { get; set; }
    }
}
