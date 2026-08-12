using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using MudBlazor;
using ResidentialComplex.Persistence;

namespace ResidentialComplex.Web.Components.Pages.Admin;

[Authorize(Roles = "Administrator")]
public partial class Users : ComponentBase
{
    [Inject] private UserManager<ApplicationUser> UserManager { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private List<UserInfo> users = new();
    private bool isLoading;

    protected override async Task OnInitializedAsync()
    {
        await LoadUsersAsync();
    }

    private async Task LoadUsersAsync()
    {
        isLoading = true;
        try
        {
            var allUsers = UserManager.Users.OrderBy(x => x.FullName).ToList();
            users = new List<UserInfo>();
            foreach (var user in allUsers)
            {
                var roles = await UserManager.GetRolesAsync(user);
                users.Add(new UserInfo
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    UserName = user.UserName ?? string.Empty,
                    Role = roles.FirstOrDefault() ?? string.Empty
                });
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بارگذاری کاربران: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void NavigateToCreate() => Navigation.NavigateTo("/admin/users/create");

    private void NavigateToEdit(string id) => Navigation.NavigateTo($"/admin/users/edit/{id}");

    private async Task DeleteUserAsync(string id)
    {
        var confirmed = await DialogService.ShowMessageBox(
            "تأیید حذف",
            "آیا از حذف این کاربر مطمئن هستید؟",
            yesText: "حذف",
            cancelText: "انصراف");
        if (confirmed != true)
        {
            return;
        }

        isLoading = true;
        try
        {
            var user = await UserManager.FindByIdAsync(id);
            if (user is null)
            {
                Snackbar.Add("کاربر یافت نشد.", Severity.Warning);
                return;
            }

            var result = await UserManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                Snackbar.Add(string.Join("، ", result.Errors.Select(x => x.Description)), Severity.Error);
                return;
            }

            await LoadUsersAsync();
            Snackbar.Add("کاربر با موفقیت حذف شد.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در حذف کاربر: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    public sealed class UserInfo
    {
        public string Id { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }
}
