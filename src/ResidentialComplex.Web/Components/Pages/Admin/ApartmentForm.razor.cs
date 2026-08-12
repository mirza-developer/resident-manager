using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Web.Components.Pages.Admin;

[Authorize(Roles = "Administrator")]
public partial class ApartmentForm : ComponentBase
{
    [Parameter] public int? Id { get; set; }

    [Inject] private IApartmentRepository ApartmentRepo { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private Apartment apartment = new();
    private bool isLoading;
    private bool notFound;

    private bool IsEditMode => Id.HasValue;

    protected override async Task OnParametersSetAsync()
    {
        if (!IsEditMode)
        {
            apartment = new Apartment();
            notFound = false;
            return;
        }

        isLoading = true;
        try
        {
            var existing = await ApartmentRepo.GetByIdAsync(Id!.Value);
            if (existing is null)
            {
                notFound = true;
                apartment = new Apartment();
                return;
            }

            apartment = new Apartment
            {
                Id = existing.Id,
                Title = existing.Title,
                Description = existing.Description,
                RowVersion = existing.RowVersion
            };
            notFound = false;
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بارگذاری آپارتمان: {ex.Message}", Severity.Error);
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
        isLoading = true;
        try
        {
            if (IsEditMode)
            {
                await ApartmentRepo.UpdateAsync(apartment);
                Snackbar.Add("آپارتمان با موفقیت بروزرسانی شد.", Severity.Success);
            }
            else
            {
                await ApartmentRepo.AddAsync(apartment);
                Snackbar.Add("آپارتمان با موفقیت ایجاد شد.", Severity.Success);
            }

            Navigation.NavigateTo("/admin/apartments");
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در ذخیره آپارتمان: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void GoBack() => Navigation.NavigateTo("/admin/apartments");
}
