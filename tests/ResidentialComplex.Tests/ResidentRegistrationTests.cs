using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Persistence;
using ResidentialComplex.Web.Security;
using Xunit;

namespace ResidentialComplex.Tests;

/// <summary>SMS provider fake: hands out a fixed/sequenced one-time code and records every send.</summary>
public class FakeSmsService : ISmsService
{
    private int _counter;

    public List<string> SentTo { get; } = new();
    public string? LastCode { get; private set; }
    public bool Fail { get; set; }
    public bool Throw { get; set; }

    public Task SendAsync(string toPhone, string text) => Task.CompletedTask;

    public Task<string?> SendOtpAsync(string toPhone)
    {
        if (Throw) throw new InvalidOperationException("provider down");
        if (Fail) return Task.FromResult<string?>(null);

        SentTo.Add(toPhone);
        LastCode = (100000 + ++_counter * 111).ToString();   // 100111, 100222, ...
        return Task.FromResult<string?>(LastCode);
    }
}

/// <summary>Minimal in-memory ASP.NET Identity store so a REAL UserManager (hashing, password policy, tokens) runs without a database.</summary>
public class InMemoryUserStore :
    IUserStore<ApplicationUser>, IUserPasswordStore<ApplicationUser>, IUserRoleStore<ApplicationUser>, IUserSecurityStampStore<ApplicationUser>,
    IUserLockoutStore<ApplicationUser>
{
    private readonly Dictionary<string, HashSet<string>> _roles = new();

    public Dictionary<string, ApplicationUser> Users { get; } = new();

    public void Dispose() { }

    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.Id);
    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.UserName);
    public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken ct) { user.UserName = userName; return Task.CompletedTask; }
    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.NormalizedUserName);
    public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken ct) { user.NormalizedUserName = normalizedName; return Task.CompletedTask; }

    public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken ct) { Users[user.Id] = user; return Task.FromResult(IdentityResult.Success); }
    public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken ct) { Users[user.Id] = user; return Task.FromResult(IdentityResult.Success); }
    public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken ct) { Users.Remove(user.Id); _roles.Remove(user.Id); return Task.FromResult(IdentityResult.Success); }
    public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken ct) => Task.FromResult(Users.GetValueOrDefault(userId));
    public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken ct) =>
        Task.FromResult(Users.Values.FirstOrDefault(u => u.NormalizedUserName == normalizedUserName));

    public Task SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken ct) { user.PasswordHash = passwordHash; return Task.CompletedTask; }
    public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.PasswordHash);
    public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.PasswordHash != null);

    public Task SetSecurityStampAsync(ApplicationUser user, string stamp, CancellationToken ct) { user.SecurityStamp = stamp; return Task.CompletedTask; }
    public Task<string?> GetSecurityStampAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.SecurityStamp);

    // lockout (used by the account-settings tests: wrong current password attempts are counted)
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.LockoutEnd);
    public Task SetLockoutEndDateAsync(ApplicationUser user, DateTimeOffset? lockoutEnd, CancellationToken ct) { user.LockoutEnd = lockoutEnd; return Task.CompletedTask; }
    public Task<int> IncrementAccessFailedCountAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(++user.AccessFailedCount);
    public Task ResetAccessFailedCountAsync(ApplicationUser user, CancellationToken ct) { user.AccessFailedCount = 0; return Task.CompletedTask; }
    public Task<int> GetAccessFailedCountAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.AccessFailedCount);
    public Task<bool> GetLockoutEnabledAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.LockoutEnabled);
    public Task SetLockoutEnabledAsync(ApplicationUser user, bool enabled, CancellationToken ct) { user.LockoutEnabled = enabled; return Task.CompletedTask; }

    // role names arrive normalized (upper case) from UserManager
    public Task AddToRoleAsync(ApplicationUser user, string roleName, CancellationToken ct)
    {
        if (!_roles.TryGetValue(user.Id, out var set)) _roles[user.Id] = set = new HashSet<string>();
        set.Add(roleName);
        return Task.CompletedTask;
    }

    public Task RemoveFromRoleAsync(ApplicationUser user, string roleName, CancellationToken ct) { _roles.GetValueOrDefault(user.Id)?.Remove(roleName); return Task.CompletedTask; }
    public Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult<IList<string>>(_roles.GetValueOrDefault(user.Id)?.ToList() ?? new List<string>());
    public Task<bool> IsInRoleAsync(ApplicationUser user, string roleName, CancellationToken ct) => Task.FromResult(_roles.GetValueOrDefault(user.Id)?.Contains(roleName) ?? false);
    public Task<IList<ApplicationUser>> GetUsersInRoleAsync(string roleName, CancellationToken ct) =>
        Task.FromResult<IList<ApplicationUser>>(Users.Values.Where(u => _roles.GetValueOrDefault(u.Id)?.Contains(roleName) ?? false).ToList());
}

/// <summary>Arranges the resident registration service with fakes (no database).</summary>
public class RegistrationEnv
{
    public const string Phone = "09123456789";

    public FakeStore Store = new();
    public FakeHouses Houses;
    public FakeSmsService Sms = new();
    public InMemoryUserStore UserStore = new();
    public FakeAudit Audit = new();
    public FakeClock Clock = new();
    public UserManager<ApplicationUser> Users;
    public ResidentRegistrationService Service;

    public RegistrationEnv()
    {
        Houses = new FakeHouses(Store);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<IUserStore<ApplicationUser>>(UserStore);
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            // same policy as Program.cs
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 6;
        }).AddDefaultTokenProviders();
        Users = services.BuildServiceProvider().GetRequiredService<UserManager<ApplicationUser>>();

        Service = new ResidentRegistrationService(Houses, Sms, Users, new MemoryCache(new MemoryCacheOptions()), Audit, Clock);
    }

    public House AddHouse(string phone = Phone, string? userId = null, string name = "علی رضایی", int id = 0)
    {
        var house = new House { Id = id == 0 ? Store.Houses.Count + 1 : id, Title = $"واحد {Store.Houses.Count + 1}", ResidentName = name, ResidentPhoneNumber = phone, ApplicationUserId = userId };
        Store.Houses.Add(house);
        return house;
    }

    public async Task<ApplicationUser> AddUserAsync(string userName, string password, params string[] roles)
    {
        var user = new ApplicationUser { UserName = userName, FullName = userName };
        var created = await Users.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join(",", created.Errors.Select(e => e.Description)));
        foreach (var role in roles) await Users.AddToRoleAsync(user, role);
        return user;
    }

    /// <summary>Request a code and verify it; returns the one-time token.</summary>
    public async Task<string> VerifiedTokenAsync(string phone = Phone)
    {
        var request = await Service.RequestOtpAsync(phone);
        Assert.True(request.Success, request.Message);
        var verify = Service.VerifyOtp(phone, Sms.LastCode);
        Assert.True(verify.Success, verify.Message);
        return verify.Token!;
    }
}

public class ResidentRegistrationTests
{
    // ------------------------------------------------------------------ requesting a code

    [Fact]
    public async Task RequestOtp_ForARegisteredPhone_SendsTheCodeToTheNormalizedNumber()
    {
        var e = new RegistrationEnv();
        e.AddHouse("0912 345 6789");

        var result = await e.Service.RequestOtpAsync("۰۹۱۲۳۴۵۶۷۸۹");   // Persian digits

        Assert.True(result.Success);
        Assert.Equal(new[] { "09123456789" }, e.Sms.SentTo);
        Assert.Contains("0912***6789", result.Message);
        Assert.DoesNotContain(e.Sms.LastCode!, result.Message);   // the code itself is never echoed back
        Assert.Contains("ResidentOtpSent", e.Audit.Actions);
    }

    [Fact]
    public async Task RequestOtp_ForAnUnknownPhone_SendsNothing()
    {
        var e = new RegistrationEnv();
        e.AddHouse("09120000000");

        var result = await e.Service.RequestOtpAsync(RegistrationEnv.Phone);

        Assert.Equal(RegistrationStatus.PhoneNotFound, result.Status);
        Assert.Empty(e.Sms.SentTo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("abc")]
    [InlineData(null)]
    public async Task RequestOtp_ForAnInvalidPhone_IsRejected(string? phone)
    {
        var e = new RegistrationEnv();
        e.AddHouse();

        var result = await e.Service.RequestOtpAsync(phone);

        Assert.Equal(RegistrationStatus.PhoneInvalid, result.Status);
        Assert.Empty(e.Sms.SentTo);
    }

    [Fact]
    public async Task RequestOtp_IsRateLimited_PerPhone()
    {
        var e = new RegistrationEnv();
        e.AddHouse();

        Assert.True((await e.Service.RequestOtpAsync(RegistrationEnv.Phone)).Success);

        var tooSoon = await e.Service.RequestOtpAsync(RegistrationEnv.Phone);
        Assert.Equal(RegistrationStatus.TooSoon, tooSoon.Status);
        Assert.InRange(tooSoon.RetryAfterSeconds, 1, 120);
        Assert.Single(e.Sms.SentTo);

        e.Clock.Advance(ResidentRegistrationService.ResendCooldown + TimeSpan.FromSeconds(1));
        Assert.True((await e.Service.RequestOtpAsync(RegistrationEnv.Phone)).Success);
    }

    [Fact]
    public async Task RequestOtp_AllowsOnlyFiveCodesPerHour()
    {
        var e = new RegistrationEnv();
        e.AddHouse();

        for (var i = 0; i < ResidentRegistrationService.MaxSendsPerWindow; i++)
        {
            Assert.True((await e.Service.RequestOtpAsync(RegistrationEnv.Phone)).Success);
            e.Clock.Advance(TimeSpan.FromMinutes(3));
        }

        var sixth = await e.Service.RequestOtpAsync(RegistrationEnv.Phone);
        Assert.Equal(RegistrationStatus.TooManyRequests, sixth.Status);
        Assert.Equal(ResidentRegistrationService.MaxSendsPerWindow, e.Sms.SentTo.Count);

        e.Clock.Advance(ResidentRegistrationService.SendWindow);
        Assert.True((await e.Service.RequestOtpAsync(RegistrationEnv.Phone)).Success);
    }

    [Fact]
    public async Task RequestOtp_WhenTheProviderFails_DoesNotConsumeTheResidentsQuota()
    {
        var e = new RegistrationEnv();
        e.AddHouse();

        e.Sms.Fail = true;
        Assert.Equal(RegistrationStatus.SmsFailed, (await e.Service.RequestOtpAsync(RegistrationEnv.Phone)).Status);
        e.Sms.Fail = false;
        e.Sms.Throw = true;
        Assert.Equal(RegistrationStatus.SmsFailed, (await e.Service.RequestOtpAsync(RegistrationEnv.Phone)).Status);
        e.Sms.Throw = false;

        Assert.True((await e.Service.RequestOtpAsync(RegistrationEnv.Phone)).Success);   // no cooldown was charged
    }

    // ------------------------------------------------------------------ checking the code

    [Fact]
    public async Task VerifyOtp_WithoutARequest_Fails()
    {
        var e = new RegistrationEnv();
        e.AddHouse();

        Assert.Equal(RegistrationStatus.OtpMissingOrExpired, e.Service.VerifyOtp(RegistrationEnv.Phone, "123456").Status);
    }

    [Fact]
    public async Task VerifyOtp_AcceptsTheCode_EvenTypedWithPersianDigits_ButOnlyOnce()
    {
        var e = new RegistrationEnv();
        e.AddHouse();
        await e.Service.RequestOtpAsync(RegistrationEnv.Phone);
        var persian = new string(e.Sms.LastCode!.Select(c => (char)('\u06F0' + (c - '0'))).ToArray());

        var ok = e.Service.VerifyOtp(RegistrationEnv.Phone, $" {persian} ");
        Assert.True(ok.Success);
        Assert.False(string.IsNullOrEmpty(ok.Token));

        Assert.False(e.Service.VerifyOtp(RegistrationEnv.Phone, e.Sms.LastCode).Success);   // single use
    }

    [Fact]
    public async Task VerifyOtp_LocksTheCodeAfterFiveWrongGuesses_EvenForTheRightCodeLater()
    {
        var e = new RegistrationEnv();
        e.AddHouse();
        await e.Service.RequestOtpAsync(RegistrationEnv.Phone);

        for (var i = 1; i < ResidentRegistrationService.MaxVerifyAttempts; i++)
            Assert.Equal(RegistrationStatus.OtpWrong, e.Service.VerifyOtp(RegistrationEnv.Phone, "000000").Status);
        Assert.Equal(RegistrationStatus.OtpLocked, e.Service.VerifyOtp(RegistrationEnv.Phone, "000000").Status);

        Assert.False(e.Service.VerifyOtp(RegistrationEnv.Phone, e.Sms.LastCode).Success);

        // a new code gives a fresh start
        e.Clock.Advance(ResidentRegistrationService.ResendCooldown + TimeSpan.FromSeconds(1));
        await e.Service.RequestOtpAsync(RegistrationEnv.Phone);
        Assert.True(e.Service.VerifyOtp(RegistrationEnv.Phone, e.Sms.LastCode).Success);
    }

    [Fact]
    public async Task VerifyOtp_RejectsAnExpiredCode()
    {
        var e = new RegistrationEnv();
        e.AddHouse();
        await e.Service.RequestOtpAsync(RegistrationEnv.Phone);
        e.Clock.Advance(ResidentRegistrationService.OtpValidity + TimeSpan.FromSeconds(1));

        Assert.Equal(RegistrationStatus.OtpMissingOrExpired, e.Service.VerifyOtp(RegistrationEnv.Phone, e.Sms.LastCode).Status);
    }

    [Fact]
    public async Task ACodeSentToOnePhone_CannotBeUsedForAnother()
    {
        var e = new RegistrationEnv();
        e.AddHouse("09121111111");
        e.AddHouse("09122222222");
        await e.Service.RequestOtpAsync("09121111111");
        var codeOfFirst = e.Sms.LastCode;
        await e.Service.RequestOtpAsync("09122222222");

        Assert.False(e.Service.VerifyOtp("09122222222", codeOfFirst).Success);
    }

    // ------------------------------------------------------------------ setting the password

    [Fact]
    public async Task SetPassword_WithoutAValidToken_ChangesNothing()
    {
        var e = new RegistrationEnv();
        var house = e.AddHouse();

        var none = await e.Service.SetPasswordAsync(RegistrationEnv.Phone, null, "Secret123", "Secret123");
        var forged = await e.Service.SetPasswordAsync(RegistrationEnv.Phone, "forged-token", "Secret123", "Secret123");
        await e.Service.RequestOtpAsync(RegistrationEnv.Phone);   // code sent but never verified
        var unverified = await e.Service.SetPasswordAsync(RegistrationEnv.Phone, "forged-token", "Secret123", "Secret123");

        Assert.All(new[] { none, forged, unverified }, r => Assert.Equal(RegistrationStatus.TokenInvalid, r.Status));
        Assert.Empty(e.UserStore.Users);
        Assert.Null(house.ApplicationUserId);
    }

    [Fact]
    public async Task SetPassword_TokenOfAnotherPhone_IsRefused()
    {
        var e = new RegistrationEnv();
        e.AddHouse("09121111111");
        e.AddHouse("09122222222");
        var tokenOfFirst = await e.VerifiedTokenAsync("09121111111");
        e.Clock.Advance(ResidentRegistrationService.ResendCooldown);
        await e.Service.RequestOtpAsync("09122222222");

        var result = await e.Service.SetPasswordAsync("09122222222", tokenOfFirst, "Secret123", "Secret123");

        Assert.Equal(RegistrationStatus.TokenInvalid, result.Status);
        Assert.Empty(e.UserStore.Users);
    }

    [Fact]
    public async Task SetPassword_WhenTheTokenExpired_IsRefused()
    {
        var e = new RegistrationEnv();
        e.AddHouse();
        var token = await e.VerifiedTokenAsync();
        e.Clock.Advance(ResidentRegistrationService.TokenValidity + TimeSpan.FromSeconds(1));

        Assert.Equal(RegistrationStatus.TokenInvalid, (await e.Service.SetPasswordAsync(RegistrationEnv.Phone, token, "Secret123", "Secret123")).Status);
    }

    [Fact]
    public async Task SignUp_CreatesAResidentAccount_AndLinksTheHouse()
    {
        var e = new RegistrationEnv();
        var house = e.AddHouse(name: "علی رضایی");
        var token = await e.VerifiedTokenAsync();

        var result = await e.Service.SetPasswordAsync(RegistrationEnv.Phone, token, "Secret123", "Secret123");

        Assert.True(result.Success, result.Message);
        Assert.True(result.CreatedNewUser);

        var user = Assert.Single(e.UserStore.Users.Values);
        Assert.Equal(RegistrationEnv.Phone, user.UserName);
        Assert.Equal("علی رضایی", user.FullName);
        Assert.True(user.PhoneNumberConfirmed);
        Assert.True(await e.Users.IsInRoleAsync(user, "Resident"));
        Assert.True(await e.Users.CheckPasswordAsync(user, "Secret123"));
        Assert.Equal(user.Id, house.ApplicationUserId);
        Assert.Contains("ResidentSignUp", e.Audit.Actions);

        // the token works once
        Assert.Equal(RegistrationStatus.TokenInvalid, (await e.Service.SetPasswordAsync(RegistrationEnv.Phone, token, "Other1234", "Other1234")).Status);
    }

    [Fact]
    public async Task SetPassword_WithMismatchingConfirmation_KeepsTheTokenUsable()
    {
        var e = new RegistrationEnv();
        e.AddHouse();
        var token = await e.VerifiedTokenAsync();

        var mismatch = await e.Service.SetPasswordAsync(RegistrationEnv.Phone, token, "Secret123", "Secret124");
        Assert.Equal(RegistrationStatus.PasswordMismatch, mismatch.Status);
        Assert.Empty(e.UserStore.Users);

        Assert.True((await e.Service.SetPasswordAsync(RegistrationEnv.Phone, token, "Secret123", "Secret123")).Success);
    }

    [Theory]
    [InlineData("abc")]          // too short, no digit
    [InlineData("abcdefgh")]     // no digit
    [InlineData("12345678")]     // no lowercase letter
    public async Task SetPassword_WithAWeakPassword_IsRejectedInPersian_AndCanBeRetried(string weak)
    {
        var e = new RegistrationEnv();
        var house = e.AddHouse();
        var token = await e.VerifiedTokenAsync();

        var rejected = await e.Service.SetPasswordAsync(RegistrationEnv.Phone, token, weak, weak);

        Assert.Equal(RegistrationStatus.PasswordRejected, rejected.Status);
        Assert.Contains("رمز عبور", rejected.Message);
        Assert.Empty(e.UserStore.Users);
        Assert.Null(house.ApplicationUserId);

        Assert.True((await e.Service.SetPasswordAsync(RegistrationEnv.Phone, token, "Secret123", "Secret123")).Success);
    }

    [Fact]
    public async Task ExistingLinkedResident_GetsANewPassword_WithoutADuplicateAccount()
    {
        var e = new RegistrationEnv();
        var user = await e.AddUserAsync(RegistrationEnv.Phone, "Old12345", "Resident");
        var oldStamp = user.SecurityStamp;
        var house = e.AddHouse(userId: user.Id);
        var token = await e.VerifiedTokenAsync();

        var result = await e.Service.SetPasswordAsync(RegistrationEnv.Phone, token, "Brand9876", "Brand9876");

        Assert.True(result.Success, result.Message);
        Assert.False(result.CreatedNewUser);
        Assert.Single(e.UserStore.Users);
        Assert.False(await e.Users.CheckPasswordAsync(user, "Old12345"));
        Assert.True(await e.Users.CheckPasswordAsync(user, "Brand9876"));
        Assert.NotEqual(oldStamp, user.SecurityStamp);          // existing sessions are invalidated
        Assert.Equal(user.Id, house.ApplicationUserId);
        Assert.Contains("ResidentPasswordReset", e.Audit.Actions);
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Worker")]
    public async Task StaffAccounts_CanNeverBeTakenOverFromThisPage(string role)
    {
        var e = new RegistrationEnv();
        var staff = await e.AddUserAsync("staff-user", "Old12345", role);
        e.AddHouse(userId: staff.Id);
        var token = await e.VerifiedTokenAsync();

        var result = await e.Service.SetPasswordAsync(RegistrationEnv.Phone, token, "Hacked999", "Hacked999");

        Assert.Equal(RegistrationStatus.AccountNotAllowed, result.Status);
        Assert.True(await e.Users.CheckPasswordAsync(staff, "Old12345"));
        Assert.False(await e.Users.CheckPasswordAsync(staff, "Hacked999"));
        Assert.Contains("ResidentRegistrationDenied", e.Audit.Actions);
    }

    [Fact]
    public async Task AStaffAccountNamedLikeThePhone_IsNotTakenOverEither()
    {
        var e = new RegistrationEnv();
        var admin = await e.AddUserAsync(RegistrationEnv.Phone, "Old12345", "Administrator");
        var house = e.AddHouse();   // not linked yet
        var token = await e.VerifiedTokenAsync();

        var result = await e.Service.SetPasswordAsync(RegistrationEnv.Phone, token, "Hacked999", "Hacked999");

        Assert.Equal(RegistrationStatus.AccountNotAllowed, result.Status);
        Assert.True(await e.Users.CheckPasswordAsync(admin, "Old12345"));
        Assert.Null(house.ApplicationUserId);
    }

    [Fact]
    public async Task ALeftoverResidentAccountNamedLikeThePhone_IsLinkedAndReset()
    {
        var e = new RegistrationEnv();
        var leftover = await e.AddUserAsync(RegistrationEnv.Phone, "Old12345", "Resident");
        var house = e.AddHouse();
        var token = await e.VerifiedTokenAsync();

        var result = await e.Service.SetPasswordAsync(RegistrationEnv.Phone, token, "Brand9876", "Brand9876");

        Assert.True(result.Success, result.Message);
        Assert.False(result.CreatedNewUser);
        Assert.Single(e.UserStore.Users);
        Assert.True(await e.Users.CheckPasswordAsync(leftover, "Brand9876"));
        Assert.Equal(leftover.Id, house.ApplicationUserId);
    }

    [Fact]
    public async Task AHouseLinkedToADeletedAccount_GetsANewAccountAndLink()
    {
        var e = new RegistrationEnv();
        var house = e.AddHouse(userId: "ghost-user-id");
        var token = await e.VerifiedTokenAsync();

        var result = await e.Service.SetPasswordAsync(RegistrationEnv.Phone, token, "Secret123", "Secret123");

        Assert.True(result.Success, result.Message);
        Assert.True(result.CreatedNewUser);
        var user = Assert.Single(e.UserStore.Users.Values);
        Assert.Equal(user.Id, house.ApplicationUserId);
    }

    [Fact]
    public async Task SeveralHousesWithTheSamePhone_ShareOneAccount()
    {
        var e = new RegistrationEnv();
        var first = e.AddHouse("09123456789");
        var second = e.AddHouse("+98 912 345 6789");
        var other = e.AddHouse("09129999999");
        var token = await e.VerifiedTokenAsync();

        var result = await e.Service.SetPasswordAsync(RegistrationEnv.Phone, token, "Secret123", "Secret123");

        Assert.True(result.Success, result.Message);
        var user = Assert.Single(e.UserStore.Users.Values);
        Assert.Equal(user.Id, first.ApplicationUserId);
        Assert.Equal(user.Id, second.ApplicationUserId);
        Assert.Null(other.ApplicationUserId);
    }

    [Fact]
    public async Task IfTheResidentRoleCannotBeAssigned_NoRoleLessAccountIsLeftBehind()
    {
        var e = new RegistrationEnv();
        var house = e.AddHouse();
        var token = await e.VerifiedTokenAsync();
        // make the store refuse role assignment
        var failing = new FailingRoleStore(e.UserStore);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<IUserStore<ApplicationUser>>(failing);
        services.AddIdentityCore<ApplicationUser>(o => { o.Password.RequireUppercase = false; o.Password.RequireNonAlphanumeric = false; }).AddDefaultTokenProviders();
        var users = services.BuildServiceProvider().GetRequiredService<UserManager<ApplicationUser>>();
        var service = new ResidentRegistrationService(e.Houses, e.Sms, users, new MemoryCache(new MemoryCacheOptions()), e.Audit, e.Clock);
        await service.RequestOtpAsync(RegistrationEnv.Phone);
        var verified = service.VerifyOtp(RegistrationEnv.Phone, e.Sms.LastCode);

        var result = await service.SetPasswordAsync(RegistrationEnv.Phone, verified.Token, "Secret123", "Secret123");

        Assert.Equal(RegistrationStatus.Error, result.Status);
        Assert.Empty(e.UserStore.Users);
        Assert.Null(house.ApplicationUserId);
    }

    /// <summary>Store whose role assignment is rejected (to test the rollback).</summary>
    private sealed class FailingRoleStore : IUserStore<ApplicationUser>, IUserPasswordStore<ApplicationUser>, IUserRoleStore<ApplicationUser>, IUserSecurityStampStore<ApplicationUser>
    {
        private readonly InMemoryUserStore _inner;
        public FailingRoleStore(InMemoryUserStore inner) => _inner = inner;
        public void Dispose() { }
        public Task<string> GetUserIdAsync(ApplicationUser u, CancellationToken c) => _inner.GetUserIdAsync(u, c);
        public Task<string?> GetUserNameAsync(ApplicationUser u, CancellationToken c) => _inner.GetUserNameAsync(u, c);
        public Task SetUserNameAsync(ApplicationUser u, string? n, CancellationToken c) => _inner.SetUserNameAsync(u, n, c);
        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser u, CancellationToken c) => _inner.GetNormalizedUserNameAsync(u, c);
        public Task SetNormalizedUserNameAsync(ApplicationUser u, string? n, CancellationToken c) => _inner.SetNormalizedUserNameAsync(u, n, c);
        public Task<IdentityResult> CreateAsync(ApplicationUser u, CancellationToken c) => _inner.CreateAsync(u, c);
        public Task<IdentityResult> UpdateAsync(ApplicationUser u, CancellationToken c) => _inner.UpdateAsync(u, c);
        public Task<IdentityResult> DeleteAsync(ApplicationUser u, CancellationToken c) => _inner.DeleteAsync(u, c);
        public Task<ApplicationUser?> FindByIdAsync(string id, CancellationToken c) => _inner.FindByIdAsync(id, c);
        public Task<ApplicationUser?> FindByNameAsync(string n, CancellationToken c) => _inner.FindByNameAsync(n, c);
        public Task SetPasswordHashAsync(ApplicationUser u, string? h, CancellationToken c) => _inner.SetPasswordHashAsync(u, h, c);
        public Task<string?> GetPasswordHashAsync(ApplicationUser u, CancellationToken c) => _inner.GetPasswordHashAsync(u, c);
        public Task<bool> HasPasswordAsync(ApplicationUser u, CancellationToken c) => _inner.HasPasswordAsync(u, c);
        public Task SetSecurityStampAsync(ApplicationUser u, string s, CancellationToken c) => _inner.SetSecurityStampAsync(u, s, c);
        public Task<string?> GetSecurityStampAsync(ApplicationUser u, CancellationToken c) => _inner.GetSecurityStampAsync(u, c);
        public Task AddToRoleAsync(ApplicationUser u, string r, CancellationToken c) => throw new InvalidOperationException("role store down");
        public Task RemoveFromRoleAsync(ApplicationUser u, string r, CancellationToken c) => _inner.RemoveFromRoleAsync(u, r, c);
        public Task<IList<string>> GetRolesAsync(ApplicationUser u, CancellationToken c) => _inner.GetRolesAsync(u, c);
        public Task<bool> IsInRoleAsync(ApplicationUser u, string r, CancellationToken c) => _inner.IsInRoleAsync(u, r, c);
        public Task<IList<ApplicationUser>> GetUsersInRoleAsync(string r, CancellationToken c) => _inner.GetUsersInRoleAsync(r, c);
    }
}
