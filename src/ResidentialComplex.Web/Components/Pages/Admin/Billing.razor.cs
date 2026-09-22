using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using MudBlazor;
using ResidentialComplex.Application.Helpers;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Application.Services;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Domain.Enums;
using ResidentialComplex.Persistence;

namespace ResidentialComplex.Web.Components.Pages.Admin;

[Authorize(Roles = "Administrator")]
public partial class Billing 
{
    [Inject] private BillingService BillingService { get; set; } = default!;
    [Inject] private IFinancialItemRepository FinancialItemRepo { get; set; } = default!;
    [Inject] private IBillRepository BillRepo { get; set; } = default!;
    [Inject] private IHouseRepository HouseRepo { get; set; } = default!;
    [Inject] private IMonthlyUsageRepository UsageRepo { get; set; } = default!;
    [Inject] private UserManager<ApplicationUser> UserManager { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private int year = PersianCalendarHelper.GetCurrentYear();
    private int month = PersianCalendarHelper.GetCurrentMonth();
    private List<FinancialItem> activeItems = new();
    private List<FinancialItemAmountRow> financialAmountRows = new();
    private List<Bill> bills = new();
    private Dictionary<(int HouseId, int FinancialItemId), int> usageByHouseItem = new();
    private bool isLoading;

    protected override async Task OnInitializedAsync()
    {
        await InitializeDataAsync();
    }

    private async Task InitializeDataAsync()
    {
        isLoading = true;
        try
        {
            activeItems = await FinancialItemRepo.GetActiveAsync();

            financialAmountRows = activeItems.Select(item => new FinancialItemAmountRow
            {
                Id = item.Id,
                Title = item.Title,
                PeriodType = item.PeriodType,
                CalculationType = item.CalculationType,
                Amount = item.TotalAmount ?? 0m
            }).ToList();

            await LoadMissingUsageDataAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بارگذاری اطلاعات صورتحساب: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadMissingUsageDataAsync()
    {
        var groupingItems = activeItems.Where(item => item.CalculationType == CalculationType.Grouping && IsApplicable(item)).ToList();
        if (!groupingItems.Any())
        {
            return;
        }

        foreach (var item in groupingItems)
        {
            if (!item.Tiers.Any())
            {
                Snackbar.Add("ابتدا مقادیر مصرف تمام آیتم‌های گروه‌بندی را ثبت کنید.", Severity.Warning);
            }
        }

        var itemsWithTiers = groupingItems.Where(item => item.Tiers.Any()).ToList();

        if (!itemsWithTiers.Any())
        {
            return;
        }

        var activeHouses = await HouseRepo.GetActiveHousesAsync();
        if (activeHouses.Count == 0)
        {
            return;
        }

        var usages = await UsageRepo.GetByMonthYearAsync(year, month);

        var usageSet = usages.Select(usage => (usage.HouseId, usage.FinancialItemId)).ToHashSet();

        foreach (var item in itemsWithTiers)
        {
            var missingCount = activeHouses.Count(house => !usageSet.Contains((house.Id, item.Id)));

            if (missingCount > 0)
            {
                Snackbar.Add($"{item.Title} — {missingCount} واحد بدون مقدار مصرف", Severity.Error);
            }
        }
    }

    private async Task OpenGenerateDialogAsync()
    {
        var parameters = new DialogParameters
        {
            [nameof(BillingGenerateDialog.InitialYear)] = year,
            [nameof(BillingGenerateDialog.InitialMonth)] = month,
            [nameof(BillingGenerateDialog.FinancialAmountRows)] = financialAmountRows,
            [nameof(BillingGenerateDialog.OnGenerate)] = async (int y, int m) =>
            {
                year = y;
                month = m;
                await GenerateAsync();
            }
        };
        var options = new DialogOptions { MaxWidth = MaxWidth.Large, FullWidth = true, CloseButton = true };
        await DialogService.ShowAsync<BillingGenerateDialog>(string.Empty, parameters, options);
    }

    private async Task GenerateAsync()
    {
        isLoading = true;
        try
        {
            await LoadMissingUsageDataAsync();

            var finalAmounts = financialAmountRows.ToDictionary(row => row.Id, row => row.Amount);
        
            var (userId, userName) = await GetCurrentUserAsync();

            var generated = await BillingService.GenerateBillsAsync(year, month, finalAmounts, userId, userName);

            Snackbar.Add($"{generated.Count} قبض تولید شد.", Severity.Success);

            await LoadBillsCoreAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در تولید قبوض: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadBillsAsync()
    {
        isLoading = true;
        try
        {
            await LoadBillsCoreAsync();
            Snackbar.Add("قبوض بارگذاری شدند.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بارگذاری قبوض: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadBillsCoreAsync()
    {
        bills = await BillRepo.GetByMonthYearAsync(year, month);
        var usages = await UsageRepo.GetByMonthYearAsync(year, month);
        usageByHouseItem = usages
            .GroupBy(usage => (usage.HouseId, usage.FinancialItemId))
            .ToDictionary(group => group.Key, group => group.First().UsageCount);
    }

    private async Task ApproveBillsAsync()
    {
        var confirmed = await DialogService.ShowMessageBox(
            "تأیید قبوض",
            "آیا از تایید همه قبوض پیش‌نویس این دوره مطمئن هستید؟",
            yesText: "تأیید",
            cancelText: "انصراف");
        if (confirmed != true)
        {
            return;
        }

        isLoading = true;
        try
        {
            var (userId, userName) = await GetCurrentUserAsync();
            
            await BillingService.ApproveBillsAsync(year, month, userId, userName);
            
            await LoadBillsCoreAsync();
            
            activeItems = await FinancialItemRepo.GetActiveAsync();
            
            financialAmountRows = activeItems.Select(item => new FinancialItemAmountRow
            {
                Id = item.Id,
                Title = item.Title,
                PeriodType = item.PeriodType,
                CalculationType = item.CalculationType,
                Amount = item.TotalAmount ?? 0m
            }).ToList();
        
            Snackbar.Add("قبوض تایید شدند.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در تایید قبوض: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;

            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task ResendSms(int billId)
    {
        isLoading = true;
        try
        {
            var (userId, userName) = await GetCurrentUserAsync();
            await BillingService.ResendBillSmsAsync(billId, userId, userName);
            Snackbar.Add("پیامک با موفقیت ارسال شد.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در ارسال پیامک: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task PayAsync(int billId)
    {
        var confirmed = await DialogService.ShowMessageBox(
            "ثبت پرداخت",
            "آیا از ثبت پرداخت این قبض مطمئن هستید؟",
            yesText: "پرداخت",
            cancelText: "انصراف");
        if (confirmed != true)
        {
            return;
        }

        isLoading = true;
        try
        {
            var (userId, userName) = await GetCurrentUserAsync();

            await BillingService.RecordPaymentAsync(billId, userId, userName);

            await LoadBillsCoreAsync();

            Snackbar.Add("پرداخت قبض ثبت شد.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در ثبت پرداخت: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task UpdateBillAmountAsync(Bill bill, decimal value)
    {
        isLoading = true;
        try
        {
            bill.TotalAmount = value;

            await BillRepo.UpdateAsync(bill);
            
            await LoadBillsCoreAsync();
            
            Snackbar.Add("مبلغ قبض بروزرسانی شد.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بروزرسانی مبلغ قبض: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;

            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task ShowBillDetails(int billId)
    {
        var bill = bills.FirstOrDefault(b => b.Id == billId);
        if (bill is null)
        {
            return;
        }

        var parameters = new DialogParameters
        {
            [nameof(BillingGenerateDialog.Mode)] = BillingGenerateDialog.BillingDialogMode.Details,
            [nameof(BillingGenerateDialog.Bill)] = bill,
            [nameof(BillingGenerateDialog.UsageByHouseItem)] = usageByHouseItem
        };
        var options = new DialogOptions { MaxWidth = MaxWidth.Large, FullWidth = true, CloseButton = true };
        await DialogService.ShowAsync<BillingGenerateDialog>(string.Empty, parameters, options);
    }

    private async Task<(string userId, string userName)> GetCurrentUserAsync()
    {
        var auth = await AuthState.GetAuthenticationStateAsync();
        var user = await UserManager.GetUserAsync(auth.User);
        return (user?.Id ?? string.Empty, user?.UserName ?? string.Empty);
    }

    private static bool IsApplicable(FinancialItem item)
    {
        if (!item.IsActive)
        {
            return false;
        }

        return item.PeriodType != PeriodType.Installment
               || !item.NumberOfInstallments.HasValue
               || item.InstallmentsBilled < item.NumberOfInstallments.Value;
    }

    /// <summary>
    /// Bill status/detail rendering (calculation labels, tier lookup) now lives entirely in
    /// BillingGenerateDialog.razor.cs, since that dialog renders bill details directly.
    /// </summary>
    private static string GetStatusLabel(BillStatus status) => status switch
    {
        BillStatus.Draft => "پیش‌نویس",
        BillStatus.Approved => "تایید شده",
        BillStatus.Paid => "پرداخت شده",
        _ => string.Empty
    };

    public sealed class FinancialItemAmountRow
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public PeriodType PeriodType { get; set; }
        public CalculationType CalculationType { get; set; }
        public decimal Amount { get; set; }
    }
}
