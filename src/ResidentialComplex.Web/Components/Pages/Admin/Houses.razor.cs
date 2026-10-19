using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Web.Components.Pages.Admin;

[Authorize(Roles = "Administrator")]
public partial class Houses : ComponentBase
{
    [Inject] private IHouseRepository HouseRepo { get; set; } = default!;
    [Inject] private IApartmentRepository ApartmentRepo { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private const string DefaultResidentPassword = "Aa123456";
    private const string ResidentRole = "Resident";

    private List<House> houses = new();
    private List<House> filteredHouses = new();
    private List<Apartment> apartments = new();
    private bool isLoading;
    private int filterApartmentId;

    protected override async Task OnInitializedAsync()
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        isLoading = true;
        try
        {
            houses = await HouseRepo.GetAllAsync();
            apartments = await ApartmentRepo.GetAllAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بارگذاری واحدها: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void ApplyFilter()
    {
        filteredHouses = filterApartmentId == 0
            ? houses
            : houses.Where(h => h.ApartmentId == filterApartmentId).ToList();
    }

    private void OnApartmentFilterChanged(int value)
    {
        filterApartmentId = value;
        ApplyFilter();
    }

    private async Task CreateResidentAccountsAsync()
    {
        var confirmed = await DialogService.ShowMessageBox(
            "ایجاد حساب کاربری",
            "برای واحدهای بدون حساب کاربری، کاربر با نام کاربری شماره تلفن ساکن و رمز عبور پیش‌فرض ایجاد می‌شود. ادامه می‌دهید؟",
            yesText: "ادامه",
            cancelText: "انصراف");
        if (confirmed != true)
        {
            return;
        }

        isLoading = true;
        try
        {
            var result = await HouseRepo.CreateResidentAccountsAsync(DefaultResidentPassword, ResidentRole);

            houses = await HouseRepo.GetAllAsync();
            ApplyFilter();
            Snackbar.Add(
                result.LinkedHouses == 0 && result.CreatedUsers == 0 && result.SkippedHouses == 0
                    ? "همه واحدها دارای حساب کاربری هستند."
                    : $"{result.CreatedUsers} کاربر ایجاد و {result.LinkedHouses} واحد متصل شد. ردشده: {result.SkippedHouses}",
                Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در ایجاد حساب کاربری: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void NavigateToCreate() => Navigation.NavigateTo("/admin/houses/create");

    private void NavigateToEdit(int id) => Navigation.NavigateTo($"/admin/houses/edit/{id}");

    private async Task DeleteHouseAsync(int id)
    {
        var confirmed = await DialogService.ShowMessageBox(
            "تأیید حذف",
            "آیا از حذف این واحد مطمئن هستید؟",
            yesText: "حذف",
            cancelText: "انصراف");
        if (confirmed != true)
        {
            return;
        }

        isLoading = true;
        try
        {
            await HouseRepo.DeleteAsync(id);
            houses = await HouseRepo.GetAllAsync();
            ApplyFilter();
            Snackbar.Add("واحد با موفقیت حذف شد.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در حذف واحد: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }
}
