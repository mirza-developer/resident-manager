using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Web.Components.Pages.Admin;

[Authorize(Roles = "Administrator")]
public partial class Apartments : ComponentBase
{
    [Inject] private IApartmentRepository ApartmentRepo { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private List<Apartment> apartments = new();
    private bool isLoading;

    protected override async Task OnInitializedAsync()
    {
        await LoadApartmentsAsync();
    }

    private async Task LoadApartmentsAsync()
    {
        isLoading = true;
        try
        {
            apartments = await ApartmentRepo.GetAllAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بارگذاری آپارتمان‌ها: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void NavigateToCreate() => Navigation.NavigateTo("/admin/apartments/create");

    private void NavigateToEdit(int id) => Navigation.NavigateTo($"/admin/apartments/edit/{id}");

    private async Task DeleteApartmentAsync(int id)
    {
        var confirmed = await DialogService.ShowMessageBox(
            "تأیید حذف",
            "آیا از حذف این آپارتمان مطمئن هستید؟",
            yesText: "حذف",
            cancelText: "انصراف");
        if (confirmed != true)
        {
            return;
        }

        isLoading = true;
        try
        {
            await ApartmentRepo.DeleteAsync(id);
            apartments = await ApartmentRepo.GetAllAsync();
            Snackbar.Add("آپارتمان با موفقیت حذف شد.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در حذف آپارتمان: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }
}
