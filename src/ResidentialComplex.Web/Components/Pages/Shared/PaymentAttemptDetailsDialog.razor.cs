using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using ResidentialComplex.Application.Services;
using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Web.Components.Pages.Shared;

/// <summary>
/// Details of one payment attempt. Residents get a summary of their own attempts; administrators
/// additionally get gateway internals and the full event trace. Access is always enforced by
/// <see cref="PaymentService.GetForUserAsync"/> using the real signed-in user — the
/// <see cref="IsAdminView"/> parameter only chooses how much to show and never grants access.
/// </summary>
public partial class PaymentAttemptDetailsDialog : ComponentBase
{
    [CascadingParameter] private MudDialogInstance MudDialog { get; set; } = default!;
    [Inject] private PaymentService PaymentService { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    [Parameter] public Guid PublicId { get; set; }
    [Parameter] public bool IsAdminView { get; set; }

    private PaymentAttempt? attempt;
    private bool isLoading = true;
    private bool showTechnical;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var user = (await AuthState.GetAuthenticationStateAsync()).User;
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var isAdmin = user.IsInRole("Administrator");

            attempt = await PaymentService.GetForUserAsync(PublicId, userId, isAdmin);
            showTechnical = IsAdminView && isAdmin && attempt is not null;
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بارگذاری جزئیات پرداخت: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
        }
    }

    private void Close() => MudDialog.Close();
}
