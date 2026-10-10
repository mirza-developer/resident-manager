using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using MudBlazor;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Persistence;

namespace ResidentialComplex.Web.Components.Pages.Resident;

public partial class Payments : ComponentBase
{
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;
    [Inject] private UserManager<ApplicationUser> UserManager { get; set; } = default!;
    [Inject] private IHouseRepository HouseRepo { get; set; } = default!;
    [Inject] private IPaymentAttemptRepository AttemptRepo { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private List<PaymentAttempt> attempts = new();
    private bool isLoading = true;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var principal = (await AuthState.GetAuthenticationStateAsync()).User;
            if (principal.IsInRole("Administrator"))
            {
                Navigation.NavigateTo("/admin/payments", replace: true);
                return;
            }

            var user = await UserManager.GetUserAsync(principal);
            if (user is null)
            {
                return;
            }

            var house = await HouseRepo.GetByUserIdAsync(user.Id);
            if (house is not null)
            {
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
        }
    }

    private static string GetResultUrl(Guid publicId) => $"/payments/result/{publicId}";
}
