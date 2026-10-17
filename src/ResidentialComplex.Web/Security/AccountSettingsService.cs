using Microsoft.AspNetCore.Identity;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Persistence;

namespace ResidentialComplex.Web.Security;

public sealed record AccountProfile(string UserName, string FullName);

public sealed record AccountSettingsResult(bool Success, string Message);

/// <summary>
/// Lets ANY signed-in user (Administrator, Worker or Resident) manage his/her OWN account: display name and password.
/// The user is always identified by the id the caller passes in (taken from the signed-in principal, never from the UI),
/// so one user can never change another user's account through this service.
///
/// Security rules:
/// * the current password is required and checked with Identity's lockout counting (5 wrong attempts → temporary lock),
///   so a hijacked session cannot be used to guess the password;
/// * the new password must satisfy the Identity password policy (Persian messages) and differ from the current one;
/// * changing the password rotates the security stamp (other sessions become invalid) — the page signs the user out afterwards.
/// </summary>
public sealed class AccountSettingsService
{
    public const int MaxFullNameLength = 100;

    private readonly UserManager<ApplicationUser> _users;
    private readonly IAuditService _audit;

    public AccountSettingsService(UserManager<ApplicationUser> users, IAuditService audit)
    {
        _users = users;
        _audit = audit;
    }

    public async Task<AccountProfile?> GetProfileAsync(string? userId)
    {
        var user = await FindAsync(userId);
        return user is null ? null : new AccountProfile(user.UserName ?? string.Empty, user.FullName);
    }

    public async Task<AccountSettingsResult> UpdateProfileAsync(string? userId, string? fullName)
    {
        var name = fullName?.Trim();
        if (string.IsNullOrEmpty(name))
            return Fail("نام کامل را وارد کنید.");
        if (name.Length > MaxFullNameLength)
            return Fail($"نام کامل نباید بیشتر از {MaxFullNameLength} کاراکتر باشد.");

        var user = await FindAsync(userId);
        if (user is null)
            return Fail("حساب کاربری یافت نشد.");

        var old = user.FullName;
        user.FullName = name;
        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            user.FullName = old;
            return Fail(string.Join("، ", result.Errors.Select(e => e.Description)));
        }

        await SafeAuditAsync(user, "AccountProfileUpdated", old, name);
        return new AccountSettingsResult(true, "اطلاعات حساب شما بروزرسانی شد.");
    }

    public async Task<AccountSettingsResult> ChangePasswordAsync(
        string? userId, string? currentPassword, string? newPassword, string? confirmPassword)
    {
        if (string.IsNullOrEmpty(currentPassword))
            return Fail("رمز عبور فعلی را وارد کنید.");
        if (string.IsNullOrEmpty(newPassword) || newPassword != confirmPassword)
            return Fail("رمز عبور جدید و تکرار آن یکسان نیستند.");
        if (newPassword == currentPassword)
            return Fail("رمز عبور جدید باید با رمز عبور فعلی متفاوت باشد.");

        var user = await FindAsync(userId);
        if (user is null)
            return Fail("حساب کاربری یافت نشد.");

        var lockoutSupported = _users.SupportsUserLockout;
        if (lockoutSupported && await _users.IsLockedOutAsync(user))
            return Fail("به دلیل چند تلاش ناموفق، تغییر رمز عبور موقتاً مسدود شد. چند دقیقه بعد دوباره تلاش کنید.");

        if (!await _users.CheckPasswordAsync(user, currentPassword))
        {
            if (lockoutSupported) await _users.AccessFailedAsync(user);
            return Fail("رمز عبور فعلی صحیح نیست.");
        }

        if (lockoutSupported) await _users.ResetAccessFailedCountAsync(user);

        var result = await _users.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
            return Fail(ResidentRegistrationService.DescribePasswordErrors(result));

        await SafeAuditAsync(user, "AccountPasswordChanged", null, null);
        return new AccountSettingsResult(true, "رمز عبور شما با موفقیت تغییر کرد.");
    }

    private async Task<ApplicationUser?> FindAsync(string? userId) =>
        string.IsNullOrWhiteSpace(userId) ? null : await _users.FindByIdAsync(userId);

    private static AccountSettingsResult Fail(string message) => new(false, message);

    private async Task SafeAuditAsync(ApplicationUser user, string action, string? oldValue, string? newValue)
    {
        try { await _audit.LogAsync(user.Id, user.UserName ?? user.Id, nameof(ApplicationUser), user.Id, action, oldValue, newValue); }
        catch { /* auditing must never break the account page */ }
    }
}

/// <summary>Persian description of the configured Identity password policy (shared by the sign-up and profile pages).</summary>
public static class PasswordPolicyHint
{
    public static string Describe(PasswordOptions rules)
    {
        var parts = new List<string> { $"حداقل {rules.RequiredLength} کاراکتر" };
        if (rules.RequireDigit) parts.Add("حداقل یک عدد");
        if (rules.RequireLowercase) parts.Add("حداقل یک حرف کوچک انگلیسی");
        if (rules.RequireUppercase) parts.Add("حداقل یک حرف بزرگ انگلیسی");
        if (rules.RequireNonAlphanumeric) parts.Add("حداقل یک نماد (مثل @ یا #)");
        return $"یک رمز عبور مناسب انتخاب کنید: {string.Join("، ", parts)}.";
    }
}
