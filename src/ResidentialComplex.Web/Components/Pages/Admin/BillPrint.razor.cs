using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Domain.Enums;

namespace ResidentialComplex.Web.Components.Pages.Admin;

/// <summary>
/// Printable A5 view of approved bills: <c>/admin/billing/print?ids=1,2,3</c>. Only bills that are
/// Approved or Paid are printed (drafts are ignored), one A5 sheet per bill, each listing all its items.
/// </summary>
[Authorize(Roles = "Administrator")]
public partial class BillPrint : ComponentBase
{
    private const string PersianDigits = "۰۱۲۳۴۵۶۷۸۹";
    private const int MaxBills = 300;

    [SupplyParameterFromQuery(Name = "ids")] public string? Ids { get; set; }

    [Inject] private IBillRepository BillRepo { get; set; } = default!;
    [Inject] private IHouseRepository HouseRepo { get; set; } = default!;
    [Inject] private IMonthlyUsageRepository UsageRepo { get; set; } = default!;
    [Inject] private IConfiguration Configuration { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private sealed record BillSheet(Bill Bill, House? House, Dictionary<int, int> UsageByItem);

    private List<BillSheet> sheets = new();
    private bool isLoading = true;
    private bool autoPrinted;
    private int skippedCount;
    private string complexName = string.Empty;

    protected override async Task OnParametersSetAsync()
    {
        isLoading = true;
        autoPrinted = false;
        complexName = Configuration["ComplexName"] ?? string.Empty;
        try
        {
            await LoadAsync();
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task LoadAsync()
    {
        sheets = new List<BillSheet>();
        skippedCount = 0;

        var requested = (Ids ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var id) && id > 0 ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        var toLoad = requested.Take(MaxBills).ToList();
        skippedCount = requested.Count - toLoad.Count;

        var bills = new List<Bill>();
        foreach (var id in toLoad)
        {
            var bill = await BillRepo.GetByIdAsync(id);
            if (bill is not null && bill.Status >= BillStatus.Approved)
            {
                bills.Add(bill);
            }
            else
            {
                skippedCount++;
            }
        }

        if (bills.Count == 0)
        {
            return;
        }

        // Houses come back in their manual order (SortOrder); the sheets follow it.
        var houses = await HouseRepo.GetAllAsync();
        var houseOrder = houses.Select((h, index) => (h.Id, index)).ToDictionary(x => x.Id, x => x.index);
        var houseById = houses.ToDictionary(h => h.Id);

        var usageByPeriod = new Dictionary<(int Year, int Month), Dictionary<(int HouseId, int FinancialItemId), int>>();
        foreach (var period in bills.Select(b => (b.Year, b.Month)).Distinct())
        {
            var usages = await UsageRepo.GetByMonthYearAsync(period.Year, period.Month);
            usageByPeriod[period] = usages
                .GroupBy(u => (u.HouseId, u.FinancialItemId))
                .ToDictionary(g => g.Key, g => g.First().UsageCount);
        }

        sheets = bills
            .OrderBy(b => houseOrder.GetValueOrDefault(b.HouseId, int.MaxValue))
            .ThenBy(b => b.Id)
            .Select(b =>
            {
                var usage = usageByPeriod[(b.Year, b.Month)]
                    .Where(kv => kv.Key.HouseId == b.HouseId)
                    .ToDictionary(kv => kv.Key.FinancialItemId, kv => kv.Value);
                return new BillSheet(b, houseById.GetValueOrDefault(b.HouseId), usage);
            })
            .ToList();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!isLoading && sheets.Count > 0 && !autoPrinted)
        {
            autoPrinted = true;
            // Give the Vazirmatn font a moment to load so the print preview already uses it.
            await Task.Delay(600);
            await PrintAsync();
        }
    }

    private async Task PrintAsync() => await JS.InvokeVoidAsync("print");

    private static string GetItemNote(BillSheet sheet, BillItem item)
    {
        var notes = new List<string>();
        if (item.FinancialItem?.CalculationType == CalculationType.ParentChildTiering
            && sheet.UsageByItem.TryGetValue(item.FinancialItemId, out var usage))
        {
            notes.Add($"مصرف: {Fa(usage.ToString())}");
        }

        if (!string.IsNullOrWhiteSpace(item.Description))
        {
            notes.Add(item.Description!);
        }

        return string.Join(" — ", notes);
    }

    private static string Money(decimal value) => Fa(value.ToString("N0"));

    /// <summary>Latin digits to Persian digits (display only).</summary>
    private static string Fa(string text) =>
        new(text.Select(c => c is >= '0' and <= '9' ? PersianDigits[c - '0'] : c).ToArray());
}
