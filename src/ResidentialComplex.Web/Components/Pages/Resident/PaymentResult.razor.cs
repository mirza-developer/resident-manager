using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using ResidentialComplex.Application.DTOs;
using ResidentialComplex.Application.Services;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Domain.Enums;

namespace ResidentialComplex.Web.Components.Pages.Resident;

/// <summary>
/// Where the user lands after the bank page (via /payment/callback). Read-only view of an attempt
/// the signed-in user is allowed to see; the page itself never decides whether a payment succeeded.
/// If the login cookie was lost on the way back, the standard authorize flow asks for a login and
/// returns to this very URL.
/// </summary>
[Authorize(Roles = "Administrator,Resident")]
public partial class PaymentResult : ComponentBase
{
    [Parameter] public Guid? PublicId { get; set; }

    [Inject] private PaymentService PaymentService { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private PaymentAttempt? attempt;
    private string? message;
    private string userId = string.Empty;
    private string userName = string.Empty;
    private bool isAdmin;
    private bool isLoading = true;
    private bool isRefreshing;

    private string HomeUrl => isAdmin ? "/admin/payments" : "/resident/dashboard";

    private bool CanRetry => attempt is not null && !isAdmin
        && attempt.Status is PaymentAttemptStatus.Failed or PaymentAttemptStatus.Expired or PaymentAttemptStatus.RequestFailed
        && attempt.Bill?.Status == BillStatus.Approved;

    private string ResultTitle => attempt?.Status switch
    {
        PaymentAttemptStatus.Succeeded => "پرداخت با موفقیت انجام شد",
        PaymentAttemptStatus.Failed or PaymentAttemptStatus.RequestFailed => "پرداخت ناموفق بود",
        PaymentAttemptStatus.Expired => "پرداخت منقضی شد",
        PaymentAttemptStatus.NeedsReview => "پرداخت در انتظار بررسی مدیر",
        _ => "پرداخت در حال بررسی است"
    };

    private string ResultIcon => attempt?.Status switch
    {
        PaymentAttemptStatus.Succeeded => Icons.Material.Filled.CheckCircle,
        PaymentAttemptStatus.Failed or PaymentAttemptStatus.RequestFailed or PaymentAttemptStatus.Expired => Icons.Material.Filled.Cancel,
        PaymentAttemptStatus.NeedsReview => Icons.Material.Filled.Warning,
        _ => Icons.Material.Filled.HourglassTop
    };

    protected override async Task OnParametersSetAsync()
    {
        isLoading = true;
        try
        {
            var user = (await AuthState.GetAuthenticationStateAsync()).User;
            userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            userName = user.Identity?.Name ?? string.Empty;
            isAdmin = user.IsInRole("Administrator");

            attempt = PublicId.HasValue ? await PaymentService.GetForUserAsync(PublicId.Value, userId, isAdmin) : null;
            message = attempt is null ? null : PaymentService.DescribeFinal(attempt);
            if (attempt is not null && PaymentService.IsOpen(attempt.Status))
                message = "پرداخت هنوز نهایی نشده است. اگر مبلغ از حساب شما کسر شده، دکمه «استعلام وضعیت» را بزنید.";
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بارگذاری نتیجه پرداخت: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task RefreshAsync()
    {
        if (attempt is null) return;

        isRefreshing = true;
        try
        {
            // Ownership is enforced again inside the service (lookup by public id + owner/admin check).
            var result = await PaymentService.ReconcileForUserAsync(attempt.PublicId, userId, userName, isAdmin);
            Snackbar.Add(result.Message, result.Outcome == PaymentProcessingOutcome.Succeeded ? Severity.Success : Severity.Info);
            attempt = PublicId.HasValue ? await PaymentService.GetForUserAsync(PublicId.Value, userId, isAdmin) : null;
            message = attempt is null ? null : result.Message;
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در استعلام وضعیت: {ex.Message}", Severity.Error);
        }
        finally
        {
            isRefreshing = false;
        }
    }
}
