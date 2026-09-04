using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Web.Components.Pages.Admin;

[Authorize(Roles = "Administrator")]
public partial class HouseForm : ComponentBase
{
    [Parameter] public int? Id { get; set; }

    [Inject] private IHouseRepository HouseRepo { get; set; } = default!;
    [Inject] private IApartmentRepository ApartmentRepo { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private House house = new() { IsActive = true };
    private List<Apartment> apartments = new();
    private bool isLoading;
    private bool notFound;

    private bool IsEditMode => Id.HasValue;

    protected override async Task OnParametersSetAsync()
    {
        isLoading = true;
        try
        {
            apartments = await ApartmentRepo.GetAllAsync();
            if (!IsEditMode)
            {
                house = new House { IsActive = true };
                notFound = false;
                return;
            }

            var existing = await HouseRepo.GetByIdAsync(Id!.Value);
            if (existing is null)
            {
                notFound = true;
                house = new House { IsActive = true };
                return;
            }

            house = new House
            {
                Id = existing.Id,
                Title = existing.Title,
                ApartmentId = existing.ApartmentId,
                ResidentName = existing.ResidentName,
                ResidentPhoneNumber = existing.ResidentPhoneNumber,
                NumberOfResidents = existing.NumberOfResidents,
                IsActive = existing.IsActive,
                CurrentDebt = existing.CurrentDebt,
                RowVersion = existing.RowVersion,
                ApplicationUserId = existing.ApplicationUserId
            };
            notFound = false;
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بارگذاری اطلاعات واحد: {ex.Message}", Severity.Error);
            notFound = true;
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            if (IsEditMode)
            {
                await HouseRepo.UpdateAsync(house);
                Snackbar.Add("واحد با موفقیت بروزرسانی شد.", Severity.Success);
            }
            else
            {
                await HouseRepo.AddAsync(house);
                Snackbar.Add("واحد با موفقیت ایجاد شد.", Severity.Success);
            }

            Navigation.NavigateTo("/admin/houses");
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در ذخیره واحد: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task RemoveAsync()
    {
        try
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

            await HouseRepo.DeleteAsync(Id.Value);

            Snackbar.Add("واحد با موفقیت حذف شد.", Severity.Success);

            Navigation.NavigateTo("/admin/houses");
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در ذخیره واحد: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void GoBack() => Navigation.NavigateTo("/admin/houses");
}
