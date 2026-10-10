using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Web.Components.Pages.Shared;

namespace ResidentialComplex.Web.Components.Pages.Resident;

/// <summary>A resident's own online payment attempts (data of the signed-in user's house only).</summary>
[Authorize(Roles = "Administrator,Resident")]
public partial class Payments : ComponentBase
{
    [Inject] private IHouseRepository HouseRepo { get; set; } = default!;
    [Inject] private IPaymentAttemptRepository AttemptRepo { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private List<PaymentAttempt> attempts = new();
    private bool hasHouse = true;
    private bool isLoading;

    protected override async Task OnInitializedAsync()
    {
        isLoading = true;
        try
        {
            var user = (await AuthState.GetAuthenticationStateAsync()).User;
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            var house = string.IsNullOrEmpty(userId) ? null : await HouseRepo.GetByUserIdAsync(userId);
            hasHouse = house is not null;
            if (house is not null)
            {
                // Data is selected by the signed-in user's own house — never by a client supplied id.
                attempts = await AttemptRepo.GetByHouseIdAsync(house.Id);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بارگذاری تاریخچه پرداخت‌ها: {ex.Message}", Severity.Error);
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
            [nameof(PaymentAttemptDetailsDialog.IsAdminView)] = false
        };
        var options = new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseButton = true };
        await DialogService.ShowAsync<PaymentAttemptDetailsDialog>(string.Empty, parameters, options);
    }
}
