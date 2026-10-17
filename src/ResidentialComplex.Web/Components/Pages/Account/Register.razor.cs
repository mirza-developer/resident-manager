using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ResidentialComplex.Web.Security;

namespace ResidentialComplex.Web.Components.Pages.Account;

/// <summary>
/// Public page: a house resident signs up (or sets a new password) after proving the phone number
/// registered on the house with an SMS code. All rules live in <see cref="ResidentRegistrationService"/>;
/// this page only walks through the three steps and shows the service's messages.
/// </summary>
public partial class Register : ComponentBase
{
    [Inject] private ResidentRegistrationService Registration { get; set; } = default!;
    [Inject] private IOptions<IdentityOptions> IdentityConfig { get; set; } = default!;

    private enum RegisterStage { Phone, Otp, Password, Done }

    private RegisterStage stage = RegisterStage.Phone;
    private string? phone;
    private string? otpCode;
    private string? password;
    private string? confirmPassword;
    private string? token;
    private string? errorMessage;
    private string? infoMessage;
    private string? successMessage;
    private bool isBusy;

    private string PasswordHint => PasswordPolicyHint.Describe(IdentityConfig.Value.Password);

    private async Task OnEnterAsync(KeyboardEventArgs e, Func<Task> action)
    {
        if (e.Key == "Enter")
        {
            await action();
        }
    }

    private async Task RequestOtpAsync()
    {
        await RunAsync(async () =>
        {
            var result = await Registration.RequestOtpAsync(phone);
            if (result.Success)
            {
                otpCode = null;
                stage = RegisterStage.Otp;
                infoMessage = result.Message;
                return;
            }

            infoMessage = null;
            errorMessage = result.Message;
        });
    }

    private async Task VerifyOtpAsync()
    {
        await RunAsync(() =>
        {
            var result = Registration.VerifyOtp(phone, otpCode);
            if (result.Success)
            {
                token = result.Token;
                stage = RegisterStage.Password;
                infoMessage = result.Message;
            }
            else
            {
                errorMessage = result.Message;
            }

            return Task.CompletedTask;
        });
    }

    private async Task SetPasswordAsync()
    {
        await RunAsync(async () =>
        {
            var result = await Registration.SetPasswordAsync(phone, token, password, confirmPassword);
            if (result.Success)
            {
                successMessage = result.Message;
                stage = RegisterStage.Done;
                infoMessage = null;
                password = confirmPassword = token = null;
                return;
            }

            errorMessage = result.Message;
            if (result.Status == RegistrationStatus.TokenInvalid)
            {
                // the verification expired: start again from the phone number
                ChangePhone();
                errorMessage = result.Message;
            }
        });
    }

    private void ChangePhone()
    {
        stage = RegisterStage.Phone;
        otpCode = password = confirmPassword = token = null;
        infoMessage = errorMessage = null;
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (isBusy)
        {
            return;
        }

        isBusy = true;
        errorMessage = null;
        try
        {
            await action();
        }
        catch (Exception)
        {
            errorMessage = "خطای غیرمنتظره‌ای رخ داد. لطفاً دوباره تلاش کنید.";
        }
        finally
        {
            isBusy = false;
            await InvokeAsync(StateHasChanged);
        }
    }
}
