using Microsoft.AspNetCore.Components;
using MudBlazor;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Domain.Enums;

namespace ResidentialComplex.Web.Components.Pages.Admin;

public partial class BillingGenerateDialog
{
    public enum BillingDialogMode
    {
        Generate,
        Details
    }

    [CascadingParameter] private MudDialogInstance MudDialog { get; set; } = default!;

    [Parameter] public BillingDialogMode Mode { get; set; } = BillingDialogMode.Generate;

    // --- Generate-mode parameters ---
    [Parameter] public int InitialYear { get; set; }
    [Parameter] public int InitialMonth { get; set; }
    [Parameter] public List<Billing.FinancialItemAmountRow> FinancialAmountRows { get; set; } = [];
    [Parameter] public Func<int, int, Task> OnGenerate { get; set; } = default!;

    // --- Details-mode parameters ---
    [Parameter] public Bill? Bill { get; set; }
    [Parameter] public Dictionary<(int HouseId, int FinancialItemId), int> UsageByHouseItem { get; set; } = new();

    private int year;
    private int month;

    protected override void OnInitialized()
    {
        year = InitialYear;
        month = InitialMonth;
    }

    private async Task GenerateClickedAsync()
    {
        await OnGenerate(year, month);
        MudDialog.Close();
    }

    private void Cancel() => MudDialog.Cancel();

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
        _ => string.Empty
    };

    private string GetBillItemDetail(Bill bill, BillItem billItem)
    {
        var financialItem = billItem.FinancialItem;
        if (financialItem?.CalculationType != CalculationType.Grouping || !financialItem.Tiers.Any())
        {
            return "-";
        }

        var usage = UsageByHouseItem.GetValueOrDefault((bill.HouseId, financialItem.Id), 0);
        return $"مصرف: {usage} واحد — کل مبلغ با نرخ {GetHouseTier(financialItem.Tiers, usage)} محاسبه شده است";
    }

    /// <summary>
    /// Finds the single bracket/tier that the house's TOTAL usage falls into.
    /// Under Whole-Consumption Bracket Pricing, this one tier's rate applies to the entire
    /// usage (no splitting across tiers, unlike an Incremental Block Tariff / IBT).
    /// </summary>
    private static string GetHouseTier(ICollection<FinancialItemTier> tiers, int usage)
    {
        if (usage <= 0)
        {
            return "بدون مصرف";
        }

        var orderedTiers = tiers.OrderBy(tier => tier.TierOrder).ToList();
        long previousLimit = 0;
        foreach (var tier in orderedTiers)
        {
            var blockEnd = tier.UpperLimit ?? int.MaxValue;
            if (usage <= blockEnd)
            {
                return tier.UpperLimit.HasValue
                    ? $"تعرفه {tier.TierOrder} ({previousLimit + 1} تا {tier.UpperLimit} واحد)"
                    : $"تعرفه {tier.TierOrder} (بالاتر از {previousLimit} واحد)";
            }

            previousLimit = blockEnd;
        }

        return $"تعرفه {orderedTiers.Last().TierOrder}";
    }
}
