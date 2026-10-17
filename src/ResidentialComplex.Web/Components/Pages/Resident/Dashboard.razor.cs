using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MudBlazor;
using ResidentialComplex.Application.Helpers;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Application.Services;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Domain.Enums;
using ResidentialComplex.Infrastructure.Settings;
using ResidentialComplex.Persistence;
using ResidentialComplex.Web.Components.Pages.Admin;
using ResidentialComplex.Web.Security;

namespace ResidentialComplex.Web.Components.Pages.Resident;

[Authorize(Roles = "Administrator,Resident")]
public partial class Dashboard : ComponentBase
{
    [Inject] private IHouseRepository HouseRepo { get; set; } = default!;
    [Inject] private IBillRepository BillRepo { get; set; } = default!;
    [Inject] private IMonthlyUsageRepository UsageRepo { get; set; } = default!;
    [Inject] private UserManager<ApplicationUser> UserManager { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private PaymentService PaymentService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IOptions<ZibalOptions> ZibalConfig { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private House? house;
    private List<Bill> bills = new();
    private bool isLoading;
    private int? payingBillId;
    private string userId = string.Empty;
    private string userName = string.Empty;

    private bool CanPayOnline => PaymentService.IsGatewayConfigured;

    protected override async Task OnInitializedAsync()
    {
        isLoading = true;
        try
        {
            var auth = await AuthState.GetAuthenticationStateAsync();
            var user = await UserManager.GetUserAsync(auth.User);
            if (user is null)
            {
                return;
            }

            userId = user.Id;
            userName = user.UserName ?? string.Empty;
            house = await HouseRepo.GetByUserIdAsync(user.Id);
            if (house is not null)
            {
                bills = await BillRepo.GetByHouseIdAsync(house.Id);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بارگذاری پنل ساکن: {ex.Message}", Severity.Error);
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

        var usages = await UsageRepo.GetByMonthYearAsync(bill.Year, bill.Month);
        var usageByHouseItem = usages
            .Where(usage => usage.HouseId == bill.HouseId)
            .GroupBy(usage => (usage.HouseId, usage.FinancialItemId))
            .ToDictionary(group => group.Key, group => group.First().UsageCount);

        var parameters = new DialogParameters
        {
            [nameof(BillingGenerateDialog.Mode)] = BillingGenerateDialog.BillingDialogMode.Details,
            [nameof(BillingGenerateDialog.Bill)] = bill,
            [nameof(BillingGenerateDialog.UsageByHouseItem)] = usageByHouseItem
        };
        var options = new DialogOptions { MaxWidth = MaxWidth.Large, FullWidth = true, CloseButton = true };
        await DialogService.ShowAsync<BillingGenerateDialog>(string.Empty, parameters, options);
    }

    /// <summary>
    /// Starts an online payment for an approved bill and sends the browser to the gateway.
    /// All checks (ownership, bill state, amount) are enforced again inside PaymentService.
    /// </summary>
    private async Task PayOnlineAsync(Bill bill)
    {
        if (payingBillId.HasValue)
        {
            return;
        }

        payingBillId = bill.Id;
        try
        {
            var callbackUrl = PaymentCallbackUrl.Build(ZibalConfig.Value.CallbackBaseUrl, Navigation.BaseUri);
            var result = await PaymentService.StartPaymentAsync(bill.Id, userId, userName, callbackUrl);

            if (result.Success && !string.IsNullOrEmpty(result.RedirectUrl))
            {
                // Full page navigation to the bank/Zibal page; we come back through /payment/callback.
                Navigation.NavigateTo(result.RedirectUrl, forceLoad: true);
                return;
            }

            if (result.AlreadyPaid && result.AttemptPublicId is { } attemptId)
            {
                Navigation.NavigateTo($"/payments/result/{attemptId}");
                return;
            }

            Snackbar.Add(result.ErrorMessage ?? "امکان شروع پرداخت وجود ندارد.", result.AlreadyPaid ? Severity.Info : Severity.Warning);
            if (house is not null)
            {
                bills = await BillRepo.GetByHouseIdAsync(house.Id);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در شروع پرداخت: {ex.Message}", Severity.Error);
        }
        finally
        {
            payingBillId = null;
        }
    }

    private static string GetMonthName(int month) => PersianCalendarHelper.GetMonthName(month);

    private static string GetStatusLabel(BillStatus status) => status switch
    {
        BillStatus.Draft => "پیش‌نویس",
        BillStatus.Approved => "تایید شده",
        BillStatus.Paid => "پرداخت شده",
        _ => string.Empty
    };
}
