using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MudBlazor;
using ResidentialComplex.Web.Security;

namespace ResidentialComplex.Web.Components.Pages.Account;

/// <summary>
/// "My account": every signed-in user (Administrator, Worker, Resident) can change his/her own display name and password.
/// The account is always the signed-in principal's — nothing about *which* account is taken from the UI.
/// </summary>
[Authorize(Roles = "Administrator,Worker,Resident")]
public partial class Profile : ComponentBase
{
    [Inject] private AccountSettingsService Settings { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private IOptions<IdentityOptions> IdentityConfig { get; set; } = default!;

    private string userId = string.Empty;
    private string userName = string.Empty;
    private string? fullName;
    private List<string> roles = new();
    private string? currentPassword;
    private string? newPassword;
    private string? confirmPassword;
    private string? passwordError;
    private bool isLoading = true;
    private bool profileFound = true;
    private bool isSavingProfile;
    private bool isChangingPassword;
    private bool passwordChanged;

    private string PasswordHint => PasswordPolicyHint.Describe(IdentityConfig.Value.Password);

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var principal = (await AuthState.GetAuthenticationStateAsync()).User;
            userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

            var profile = await Settings.GetProfileAsync(userId);
            profileFound = profile is not null;
            userName = profile?.UserName ?? string.Empty;
            fullName = profile?.FullName;
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در بارگذاری اطلاعات حساب: {ex.Message}", Severity.Error);
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task OnEnterAsync(KeyboardEventArgs e, Func<Task> action)
    {
        if (e.Key == "Enter")
        {
            await action();
        }
    }

    private async Task SaveProfileAsync()
    {
        if (isSavingProfile)
        {
            return;
        }

        isSavingProfile = true;
        try
        {
            var result = await Settings.UpdateProfileAsync(userId, fullName);
            Snackbar.Add(result.Message, result.Success ? Severity.Success : Severity.Warning);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"خطا در ذخیره اطلاعات: {ex.Message}", Severity.Error);
        }
        finally
        {
            isSavingProfile = false;
        }
    }

    private async Task ChangePasswordAsync()
    {
        if (isChangingPassword || passwordChanged)
        {
            return;
        }

        isChangingPassword = true;
        passwordError = null;
        try
        {
            var result = await Settings.ChangePasswordAsync(userId, currentPassword, newPassword, confirmPassword);
            if (!result.Success)
            {
                passwordError = result.Message;
                return;
            }

            currentPassword = newPassword = confirmPassword = null;
            passwordChanged = true;
            await InvokeAsync(StateHasChanged);

            // The security stamp changed, so every session is invalid anyway: sign out and let the user log in again.
            await Task.Delay(2500);
            Navigation.NavigateTo("/Account/Logout", forceLoad: true);
        }
        catch (Exception ex)
        {
            passwordError = $"خطا در تغییر رمز عبور: {ex.Message}";
        }
        finally
        {
            isChangingPassword = false;
        }
    }

    private static string GetRoleLabel(string role) => role switch
    {
        "Administrator" => "مدیر سیستم",
        "Worker" => "کارگر (ثبت مصرف)",
        "Resident" => "ساکن",
        _ => role
    };
}
