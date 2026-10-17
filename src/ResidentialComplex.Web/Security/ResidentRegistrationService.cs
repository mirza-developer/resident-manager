using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Application.Services;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Domain.Enums;
using ResidentialComplex.Persistence;

namespace ResidentialComplex.Web.Security;

public enum RegistrationStatus
{
    Ok,
    PhoneInvalid,
    PhoneNotFound,
    TooSoon,
    TooManyRequests,
    SmsFailed,
    OtpMissingOrExpired,
    OtpWrong,
    OtpLocked,
    TokenInvalid,
    PasswordMismatch,
    PasswordRejected,
    AccountNotAllowed,
    Error
}

/// <param name="Token">One-time proof of a successful OTP check (returned by <c>VerifyOtp</c>).</param>
/// <param name="RetryAfterSeconds">For <see cref="RegistrationStatus.TooSoon"/>: seconds until a new code may be requested.</param>
public sealed record RegistrationResult(
    RegistrationStatus Status, string Message, string? Token = null, int RetryAfterSeconds = 0, bool CreatedNewUser = false)
{
    public bool Success => Status == RegistrationStatus.Ok;
}

/// <summary>
/// Lets a house resident sign up (or set a new password) from the login page, proving ownership of the
/// phone number registered on the house with an SMS one-time code (<see cref="ISmsService.SendOtpAsync"/>).
///
/// Flow: <see cref="RequestOtpAsync"/> → <see cref="VerifyOtp"/> (returns a one-time token) →
/// <see cref="SetPasswordAsync"/> (token + new password).
///
/// Security rules:
/// * the OTP is never stored in clear text (only a hash) and never returned or logged;
/// * limits per phone number, shared by every browser/circuit: 2 min between codes, 5 codes per hour,
///   5 wrong guesses per code, code valid 5 min; the OTP check hands out a token that is valid 10 min and works once;
/// * only <c>Resident</c> accounts can be created or reset here — an account that is Administrator or Worker is refused,
///   so this page can never be used to take over a staff account.
/// State is kept in <see cref="IMemoryCache"/> (lost on restart: the resident simply requests a new code).
/// </summary>
public sealed class ResidentRegistrationService
{
    public static readonly TimeSpan OtpValidity = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan SendWindow = TimeSpan.FromHours(1);
    public static readonly TimeSpan TokenValidity = TimeSpan.FromMinutes(10);
    public const int MaxSendsPerWindow = 5;
    public const int MaxVerifyAttempts = 5;

    private static readonly object CreateGate = new();

    private readonly IHouseRepository _houses;
    private readonly ISmsService _sms;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IMemoryCache _cache;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;

    public ResidentRegistrationService(
        IHouseRepository houses,
        ISmsService sms,
        UserManager<ApplicationUser> users,
        IMemoryCache cache,
        IAuditService audit,
        TimeProvider? timeProvider = null)
    {
        _houses = houses;
        _sms = sms;
        _users = users;
        _cache = cache;
        _audit = audit;
        _clock = timeProvider ?? TimeProvider.System;
    }

    private DateTime Now => _clock.GetLocalNow().DateTime;

    // =====================================================================
    // 1. Request a code
    // =====================================================================

    public async Task<RegistrationResult> RequestOtpAsync(string? phoneInput)
    {
        var phone = PaymentService.NormalizeMobile(phoneInput);
        if (phone is null)
            return Fail(RegistrationStatus.PhoneInvalid, "شماره موبایل نامعتبر است. شماره را به‌صورت 09123456789 وارد کنید.");

        if ((await FindHousesAsync(phone)).Count == 0)
            return Fail(RegistrationStatus.PhoneNotFound, "این شماره در سامانه ثبت نشده است. لطفاً با مدیر ساختمان تماس بگیرید.");

        var state = GetState(phone);
        var now = Now;
        lock (state)
        {
            state.Sends.RemoveAll(t => now - t > SendWindow);
            if (state.Sends.Count > 0 && now - state.Sends[^1] < ResendCooldown)
            {
                var wait = (int)Math.Ceiling((ResendCooldown - (now - state.Sends[^1])).TotalSeconds);
                return new RegistrationResult(RegistrationStatus.TooSoon, $"کد قبلی ارسال شده است. {wait} ثانیه دیگر می‌توانید کد جدید بگیرید.", RetryAfterSeconds: wait);
            }

            if (state.Sends.Count >= MaxSendsPerWindow)
                return Fail(RegistrationStatus.TooManyRequests, "تعداد درخواست‌های کد بیش از حد مجاز است. یک ساعت بعد دوباره تلاش کنید.");

            state.Sends.Add(now);   // reserve the slot before sending so parallel requests cannot bypass the limits
        }

        string? code;
        try
        {
            code = await _sms.SendOtpAsync(phone);
        }
        catch
        {
            code = null;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            lock (state) { state.Sends.Remove(now); }   // a failed send does not count against the resident
            return Fail(RegistrationStatus.SmsFailed, "ارسال پیامک با خطا مواجه شد. لطفاً دقایقی بعد دوباره تلاش کنید.");
        }

        lock (state)
        {
            state.OtpHash = Hash("otp", phone, NormalizeDigits(code));
            state.OtpExpiresAt = now + OtpValidity;
            state.Attempts = 0;
            state.TokenHash = null;
        }

        await SafeAuditAsync(phone, "ResidentOtpSent", null);
        return new RegistrationResult(RegistrationStatus.Ok, $"کد تایید به شماره {Mask(phone)} ارسال شد.");
    }

    // =====================================================================
    // 2. Check the code
    // =====================================================================

    public RegistrationResult VerifyOtp(string? phoneInput, string? codeInput)
    {
        var phone = PaymentService.NormalizeMobile(phoneInput);
        var code = NormalizeDigits(codeInput);
        if (phone is null || code.Length == 0 || !_cache.TryGetValue(StateKey(phone), out PhoneState? state) || state is null)
            return Fail(RegistrationStatus.OtpMissingOrExpired, "ابتدا کد تایید را درخواست کنید.");

        var now = Now;
        lock (state)
        {
            if (state.OtpHash is null || now > state.OtpExpiresAt)
                return Fail(RegistrationStatus.OtpMissingOrExpired, "کد تایید منقضی شده است. کد جدید درخواست کنید.");

            if (state.Attempts >= MaxVerifyAttempts)
                return Fail(RegistrationStatus.OtpLocked, "تعداد تلاش‌های ناموفق زیاد بود. کد جدید درخواست کنید.");

            state.Attempts++;
            if (!CryptographicOperations.FixedTimeEquals(Hash("otp", phone, code), state.OtpHash))
            {
                var left = MaxVerifyAttempts - state.Attempts;
                if (left <= 0)
                {
                    state.OtpHash = null;   // too many guesses: this code is dead, a new one is needed
                    return Fail(RegistrationStatus.OtpLocked, "تعداد تلاش‌های ناموفق زیاد بود. کد جدید درخواست کنید.");
                }

                return Fail(RegistrationStatus.OtpWrong, $"کد واردشده صحیح نیست. {left} تلاش دیگر باقی مانده است.");
            }

            // Success: the code is single use; hand out a token that proves the check to the next step.
            state.OtpHash = null;
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            state.TokenHash = Hash("token", phone, token);
            state.TokenExpiresAt = now + TokenValidity;
            return new RegistrationResult(RegistrationStatus.Ok, "شماره شما تایید شد.", Token: token);
        }
    }

    // =====================================================================
    // 3. Set the password (creates the user the first time)
    // =====================================================================

    public async Task<RegistrationResult> SetPasswordAsync(string? phoneInput, string? token, string? password, string? confirmPassword)
    {
        var phone = PaymentService.NormalizeMobile(phoneInput);
        if (phone is null || string.IsNullOrEmpty(token) || !_cache.TryGetValue(StateKey(phone), out PhoneState? state) || state is null
            || !TokenIsValid(state, phone, token))
        {
            return Fail(RegistrationStatus.TokenInvalid, "اعتبار تایید شماره به پایان رسیده است. لطفاً از ابتدا شروع کنید.");
        }

        if (string.IsNullOrEmpty(password) || password != confirmPassword)
            return Fail(RegistrationStatus.PasswordMismatch, "رمز عبور و تکرار آن یکسان نیستند.");

        var houses = await FindHousesAsync(phone);
        if (houses.Count == 0)
            return Fail(RegistrationStatus.PhoneNotFound, "این شماره در سامانه ثبت نشده است.");

        var user = await ResolveExistingUserAsync(houses, phone);
        var created = false;

        if (user is not null)
        {
            // Never touch staff accounts from this public page.
            if (await _users.IsInRoleAsync(user, AppRoles.Administrator) || await _users.IsInRoleAsync(user, AppRoles.Worker))
            {
                await SafeAuditAsync(phone, "ResidentRegistrationDenied", "staff account");
                return Fail(RegistrationStatus.AccountNotAllowed, "تعیین رمز عبور برای این حساب از این صفحه امکان‌پذیر نیست. با مدیر تماس بگیرید.");
            }

            var resetToken = await _users.GeneratePasswordResetTokenAsync(user);
            var reset = await _users.ResetPasswordAsync(user, resetToken, password);
            if (!reset.Succeeded)
                return Fail(RegistrationStatus.PasswordRejected, DescribePasswordErrors(reset));

            if (!await _users.IsInRoleAsync(user, AppRoles.Resident))
                await _users.AddToRoleAsync(user, AppRoles.Resident);
        }
        else
        {
            user = new ApplicationUser
            {
                UserName = phone,
                FullName = houses[0].ResidentName,
                PhoneNumber = phone,
                PhoneNumberConfirmed = true   // proven by the OTP
            };

            var create = await _users.CreateAsync(user, password);
            if (!create.Succeeded)
                return Fail(RegistrationStatus.PasswordRejected, DescribePasswordErrors(create));

            IdentityResult role;
            try
            {
                role = await _users.AddToRoleAsync(user, AppRoles.Resident);
            }
            catch (Exception)
            {
                // e.g. the EF store throws when the "Resident" role row does not exist
                role = IdentityResult.Failed();
            }

            if (!role.Succeeded)
            {
                await _users.DeleteAsync(user);   // never leave a role-less account behind
                return Fail(RegistrationStatus.Error, "ایجاد حساب کاربری با خطا مواجه شد. با مدیر تماس بگیرید.");
            }

            created = true;
        }

        // Link every house of this phone that has no (existing) account yet.
        foreach (var house in houses)
        {
            if (house.ApplicationUserId == user.Id) continue;

            if (string.IsNullOrEmpty(house.ApplicationUserId) || await _users.FindByIdAsync(house.ApplicationUserId) is null)
            {
                house.ApplicationUserId = user.Id;
                house.IsActive = true;
                await _houses.UpdateAsync(house);
            }
        }

        lock (state) { state.TokenHash = null; }   // the token works once

        await SafeAuditAsync(phone, created ? "ResidentSignUp" : "ResidentPasswordReset", user.Id);
        return new RegistrationResult(RegistrationStatus.Ok,
            created ? "حساب کاربری شما ساخته شد. اکنون می‌توانید وارد شوید." : "رمز عبور جدید شما ثبت شد. اکنون می‌توانید وارد شوید.",
            CreatedNewUser: created);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private async Task<List<House>> FindHousesAsync(string phone) =>
        (await _houses.GetAllAsync()).Where(h => PaymentService.NormalizeMobile(h.ResidentPhoneNumber) == phone).ToList();

    /// <summary>The account already linked to one of the houses, else a leftover account named like the phone.</summary>
    private async Task<ApplicationUser?> ResolveExistingUserAsync(List<House> houses, string phone)
    {
        foreach (var id in houses.Select(h => h.ApplicationUserId).Where(id => !string.IsNullOrEmpty(id)).Distinct())
        {
            var linked = await _users.FindByIdAsync(id!);
            if (linked is not null) return linked;
        }

        return await _users.FindByNameAsync(phone);
    }

    private bool TokenIsValid(PhoneState state, string phone, string token)
    {
        lock (state)
        {
            return state.TokenHash is not null
                   && Now <= state.TokenExpiresAt
                   && CryptographicOperations.FixedTimeEquals(Hash("token", phone, token), state.TokenHash);
        }
    }

    private static string StateKey(string phone) => $"resident-registration:{phone}";

    private PhoneState GetState(string phone)
    {
        lock (CreateGate)
        {
            if (!_cache.TryGetValue(StateKey(phone), out PhoneState? state) || state is null)
            {
                state = new PhoneState();
                _cache.Set(StateKey(phone), state, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(2) });
            }

            return state;
        }
    }

    private static byte[] Hash(string purpose, string phone, string secret) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{purpose}:{phone}:{secret}"));

    /// <summary>Persian/Arabic digits → ASCII, and drop spaces, so a code typed on a Persian keyboard still matches.</summary>
    private static string NormalizeDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var sb = new StringBuilder();
        foreach (var c in value)
        {
            if (c >= '0' && c <= '9') sb.Append(c);
            else if (c >= '\u06F0' && c <= '\u06F9') sb.Append((char)('0' + (c - '\u06F0')));
            else if (c >= '\u0660' && c <= '\u0669') sb.Append((char)('0' + (c - '\u0660')));
            else if (!char.IsWhiteSpace(c)) sb.Append(c);
        }

        return sb.ToString();
    }

    private static string Mask(string phone) => phone.Length == 11 ? $"{phone[..4]}***{phone[7..]}" : "***";

    private static RegistrationResult Fail(RegistrationStatus status, string message) => new(status, message);

    /// <summary>Persian wording for the Identity password-policy errors (falls back to Identity's own text).</summary>
    public static string DescribePasswordErrors(IdentityResult result) =>
        string.Join("، ", result.Errors.Select(e => e.Code switch
        {
            nameof(IdentityErrorDescriber.PasswordTooShort) => "رمز عبور کوتاه است",
            nameof(IdentityErrorDescriber.PasswordRequiresDigit) => "رمز عبور باید حداقل یک عدد داشته باشد",
            nameof(IdentityErrorDescriber.PasswordRequiresLower) => "رمز عبور باید حداقل یک حرف کوچک انگلیسی داشته باشد",
            nameof(IdentityErrorDescriber.PasswordRequiresUpper) => "رمز عبور باید حداقل یک حرف بزرگ انگلیسی داشته باشد",
            nameof(IdentityErrorDescriber.PasswordRequiresNonAlphanumeric) => "رمز عبور باید حداقل یک نماد (مثل @ یا #) داشته باشد",
            nameof(IdentityErrorDescriber.PasswordRequiresUniqueChars) => "رمز عبور باید کاراکترهای متنوع‌تری داشته باشد",
            _ => e.Description
        }));

    private async Task SafeAuditAsync(string phone, string action, string? detail)
    {
        try { await _audit.LogAsync("anonymous", Mask(phone), nameof(ApplicationUser), phone, action, null, detail); }
        catch { /* auditing must never break the sign-up flow */ }
    }

    private sealed class PhoneState
    {
        public readonly List<DateTime> Sends = new();
        public byte[]? OtpHash;
        public DateTime OtpExpiresAt;
        public int Attempts;
        public byte[]? TokenHash;
        public DateTime TokenExpiresAt;
    }
}
