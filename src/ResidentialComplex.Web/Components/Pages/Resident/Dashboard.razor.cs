using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using MudBlazor;
using ResidentialComplex.Application.Helpers;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Domain.Enums;
using ResidentialComplex.Persistence;

namespace ResidentialComplex.Web.Components.Pages.Resident;

[Authorize(Roles = "Administrator,Resident")]
public partial class Dashboard : ComponentBase
{
    [Inject] private IHouseRepository HouseRepo { get; set; } = default!;
    [Inject] private IBillRepository BillRepo { get; set; } = default!;
    [Inject] private UserManager<ApplicationUser> UserManager { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private House? house;
    private List<Bill> bills = new();
    private bool isLoading;

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

    private static string GetMonthName(int month) => PersianCalendarHelper.GetMonthName(month);

    private static string GetStatusLabel(BillStatus status) => status switch
    {
        BillStatus.Draft => "پیش‌نویس",
        BillStatus.Approved => "تایید شده",
        BillStatus.Paid => "پرداخت شده",
        _ => string.Empty
    };
}
