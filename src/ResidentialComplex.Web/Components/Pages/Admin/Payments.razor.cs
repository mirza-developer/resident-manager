using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using ResidentialComplex.Application.DTOs;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Application.Services;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Domain.Enums;
using ResidentialComplex.Web.Components.Pages.Shared;

namespace ResidentialComplex.Web.Components.Pages.Admin;

/// <summary>Administrator view of every online payment attempt (Zibal) with status, trace and manual inquiry.</summary>
[Authorize(Roles = "Administrator")]
public partial class Payments : ComponentBase
{
    [Inject] private IPaymentAttemptRepository AttemptRepo { get; set; } = default!;
    [Inject] private PaymentService PaymentService { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private List<PaymentAttempt> attempts = new();
    private bool isLoading;
    private int statusFilter = -1;
    private string searchText = string.Empty;
    private int? reconcilingId;

    private int succeededCount => attempts.Count(a => a.Status == PaymentAttemptStatus.Succeeded);
    private decimal succeededTotal => attempts.Where(a => a.Status == PaymentAttemptStatus.Succeeded).Sum(a => a.Amount);
    private int needsReviewCount => attempts.Count(a => a.Status == PaymentAttemptStatus.NeedsReview);
    private int openCount => attempts.Count(a => PaymentService.IsOpen(a.Status));

    private IEnumerable<PaymentAttempt> Filtered
    {
        get
        {
            var query = attempts.AsEnumerable();
            if (statusFilter >= 0)
            {
                query = query.Where(a => (int)a.Status == statusFilter);
            }

            var text = searchText?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                query = query.Where(a =>
                    (a.TrackId?.ToString().Contains(text) ?? false)
                    || (a.RefNumber?.ToString().Contains(text) ?? false)
                    || a.OrderId.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || (a.Bill?.House?.Title.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (a.Bill?.House?.Apartment?.Title.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            return query;
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        isLoading = true;
        try
        {
            attempts = await AttemptRepo.GetAllAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بارگذاری پرداخت‌ها: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task ShowDetailsAsync(Guid publicId)
    {
        var parameters = new DialogParameters
        {
            [nameof(PaymentAttemptDetailsDialog.PublicId)] = publicId,
            [nameof(PaymentAttemptDetailsDialog.IsAdminView)] = true
        };
        var options = new DialogOptions { MaxWidth = MaxWidth.Large, FullWidth = true, CloseButton = true };
        await DialogService.ShowAsync<PaymentAttemptDetailsDialog>(string.Empty, parameters, options);
    }

    private async Task ReconcileAsync(int attemptId)
    {
        reconcilingId = attemptId;
        try
        {
            var user = (await AuthState.GetAuthenticationStateAsync()).User;
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var result = await PaymentService.ReconcileAsync(attemptId, userId, user.Identity?.Name ?? string.Empty);
            Snackbar.Add(result.Message, result.Outcome == PaymentProcessingOutcome.Succeeded ? Severity.Success : Severity.Info);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در استعلام وضعیت: {ex.Message}", Severity.Error);
        }
        finally
        {
            reconcilingId = null;
        }
    }
}
