using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ResidentialComplex.Persistence;
using ResidentialComplex.Web.Security;
using Xunit;

namespace ResidentialComplex.Tests;

/// <summary>"My account" for every role: display name and password, against a real UserManager (no database).</summary>
public class AccountSettingsTests
{
    private sealed class Env
    {
        public InMemoryUserStore Store = new();
        public FakeAudit Audit = new();
        public UserManager<ApplicationUser> Users;
        public AccountSettingsService Service;

        public Env()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddSingleton<IUserStore<ApplicationUser>>(Store);
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
            Service = new AccountSettingsService(Users, Audit);
        }

        public async Task<ApplicationUser> AddUserAsync(string userName, string password, string role, string fullName = "نام اولیه")
        {
            var user = new ApplicationUser { UserName = userName, FullName = fullName };
            Assert.True((await Users.CreateAsync(user, password)).Succeeded);
            await Users.AddToRoleAsync(user, role);
            return user;
        }
    }

    // ------------------------------------------------------------------ every role can manage its own account

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Worker")]
    [InlineData("Resident")]
    public async Task EveryRole_CanChangeItsOwnPassword(string role)
    {
        var e = new Env();
        var user = await e.AddUserAsync("someone", "Old12345", role);

        var result = await e.Service.ChangePasswordAsync(user.Id, "Old12345", "Brand9876", "Brand9876");

        Assert.True(result.Success, result.Message);
        Assert.False(await e.Users.CheckPasswordAsync(user, "Old12345"));
        Assert.True(await e.Users.CheckPasswordAsync(user, "Brand9876"));
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Worker")]
    [InlineData("Resident")]
    public async Task EveryRole_CanChangeItsOwnDisplayName(string role)
    {
        var e = new Env();
        var user = await e.AddUserAsync("someone", "Old12345", role);

        var result = await e.Service.UpdateProfileAsync(user.Id, "  نام جدید  ");

        Assert.True(result.Success, result.Message);
        Assert.Equal("نام جدید", user.FullName);
        Assert.Equal("نام جدید", (await e.Service.GetProfileAsync(user.Id))!.FullName);
    }

    [Fact]
    public async Task GetProfile_ReturnsUserNameAndFullName()
    {
        var e = new Env();
        var user = await e.AddUserAsync("09123456789", "Old12345", "Resident", "علی رضایی");

        var profile = await e.Service.GetProfileAsync(user.Id);

        Assert.NotNull(profile);
        Assert.Equal("09123456789", profile!.UserName);
        Assert.Equal("علی رضایی", profile.FullName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-user-id")]
    public async Task UnknownOrMissingUserId_ChangesNothing(string? userId)
    {
        var e = new Env();
        var user = await e.AddUserAsync("someone", "Old12345", "Resident");

        Assert.Null(await e.Service.GetProfileAsync(userId));
        Assert.False((await e.Service.UpdateProfileAsync(userId, "هکر")).Success);
        Assert.False((await e.Service.ChangePasswordAsync(userId, "Old12345", "Hacked999", "Hacked999")).Success);
        Assert.Equal("نام اولیه", user.FullName);
        Assert.True(await e.Users.CheckPasswordAsync(user, "Old12345"));
    }

    [Fact]
    public async Task ChangingOneUsersPassword_NeverTouchesAnotherUser()
    {
        var e = new Env();
        var first = await e.AddUserAsync("first", "Old12345", "Resident");
        var second = await e.AddUserAsync("second", "Other12345", "Administrator");

        await e.Service.ChangePasswordAsync(first.Id, "Old12345", "Brand9876", "Brand9876");

        Assert.True(await e.Users.CheckPasswordAsync(second, "Other12345"));
        // knowing someone else's password does not help: the account is the caller's own
        var cross = await e.Service.ChangePasswordAsync(first.Id, "Other12345", "Hacked999", "Hacked999");
        Assert.False(cross.Success);
        Assert.True(await e.Users.CheckPasswordAsync(second, "Other12345"));
    }

    // ------------------------------------------------------------------ profile validation

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyName_IsRejected_AndTheOldNameIsKept(string? name)
    {
        var e = new Env();
        var user = await e.AddUserAsync("someone", "Old12345", "Resident");

        var result = await e.Service.UpdateProfileAsync(user.Id, name);

        Assert.False(result.Success);
        Assert.Equal("نام اولیه", user.FullName);
    }

    [Fact]
    public async Task TooLongName_IsRejected()
    {
        var e = new Env();
        var user = await e.AddUserAsync("someone", "Old12345", "Resident");

        var result = await e.Service.UpdateProfileAsync(user.Id, new string('ا', AccountSettingsService.MaxFullNameLength + 1));

        Assert.False(result.Success);
        Assert.Equal("نام اولیه", user.FullName);
    }

    [Fact]
    public async Task ProfileUpdate_IsAudited_WithOldAndNewName()
    {
        var e = new Env();
        var user = await e.AddUserAsync("someone", "Old12345", "Resident");

        await e.Service.UpdateProfileAsync(user.Id, "نام جدید");

        Assert.Contains("AccountProfileUpdated", e.Audit.Actions);
    }

    // ------------------------------------------------------------------ password change rules

    [Fact]
    public async Task WrongCurrentPassword_IsRejected_AndNothingChanges()
    {
        var e = new Env();
        var user = await e.AddUserAsync("someone", "Old12345", "Resident");

        var result = await e.Service.ChangePasswordAsync(user.Id, "Wrong12345", "Brand9876", "Brand9876");

        Assert.False(result.Success);
        Assert.Contains("فعلی", result.Message);
        Assert.True(await e.Users.CheckPasswordAsync(user, "Old12345"));
        Assert.Equal(1, user.AccessFailedCount);
    }

    [Fact]
    public async Task FiveWrongCurrentPasswords_LockTheChange_EvenForTheCorrectPassword()
    {
        var e = new Env();
        var user = await e.AddUserAsync("someone", "Old12345", "Resident");

        for (var i = 0; i < 5; i++)
            Assert.False((await e.Service.ChangePasswordAsync(user.Id, $"Wrong{i}12345", "Brand9876", "Brand9876")).Success);

        var locked = await e.Service.ChangePasswordAsync(user.Id, "Old12345", "Brand9876", "Brand9876");

        Assert.False(locked.Success);
        Assert.Contains("مسدود", locked.Message);
        Assert.True(await e.Users.CheckPasswordAsync(user, "Old12345"));   // password untouched
    }

    [Fact]
    public async Task CorrectCurrentPassword_ResetsTheFailedAttemptCounter()
    {
        var e = new Env();
        var user = await e.AddUserAsync("someone", "Old12345", "Resident");
        await e.Service.ChangePasswordAsync(user.Id, "Wrong12345", "Brand9876", "Brand9876");
        Assert.Equal(1, user.AccessFailedCount);

        var ok = await e.Service.ChangePasswordAsync(user.Id, "Old12345", "Brand9876", "Brand9876");

        Assert.True(ok.Success, ok.Message);
        Assert.Equal(0, user.AccessFailedCount);
    }

    [Fact]
    public async Task Mismatch_SamePassword_AndEmptyCurrent_AreRejected_WithoutCountingAFailedAttempt()
    {
        var e = new Env();
        var user = await e.AddUserAsync("someone", "Old12345", "Resident");

        Assert.False((await e.Service.ChangePasswordAsync(user.Id, "Old12345", "Brand9876", "Brand9877")).Success);
        Assert.False((await e.Service.ChangePasswordAsync(user.Id, "Old12345", "Old12345", "Old12345")).Success);
        Assert.False((await e.Service.ChangePasswordAsync(user.Id, "", "Brand9876", "Brand9876")).Success);
        Assert.False((await e.Service.ChangePasswordAsync(user.Id, "Old12345", "", "")).Success);

        Assert.True(await e.Users.CheckPasswordAsync(user, "Old12345"));
        Assert.Equal(0, user.AccessFailedCount);
    }

    [Theory]
    [InlineData("abc")]        // too short, no digit
    [InlineData("abcdefgh")]   // no digit
    [InlineData("12345678")]   // no lowercase letter
    public async Task WeakNewPassword_IsRejectedInPersian_AndTheOldOneKeepsWorking(string weak)
    {
        var e = new Env();
        var user = await e.AddUserAsync("someone", "Old12345", "Resident");

        var result = await e.Service.ChangePasswordAsync(user.Id, "Old12345", weak, weak);

        Assert.False(result.Success);
        Assert.Contains("رمز عبور", result.Message);
        Assert.True(await e.Users.CheckPasswordAsync(user, "Old12345"));
    }

    [Fact]
    public async Task PasswordChange_RotatesTheSecurityStamp_AndIsAudited()
    {
        var e = new Env();
        var user = await e.AddUserAsync("someone", "Old12345", "Resident");
        var stamp = user.SecurityStamp;

        await e.Service.ChangePasswordAsync(user.Id, "Old12345", "Brand9876", "Brand9876");

        Assert.NotEqual(stamp, user.SecurityStamp);   // other sessions become invalid
        Assert.Contains("AccountPasswordChanged", e.Audit.Actions);
    }

    // ------------------------------------------------------------------ shared password hint

    [Fact]
    public void PasswordHint_DescribesTheConfiguredPolicy()
    {
        var hint = PasswordPolicyHint.Describe(new PasswordOptions { RequiredLength = 8, RequireDigit = true, RequireLowercase = true, RequireUppercase = true, RequireNonAlphanumeric = true });

        Assert.Contains("8", hint);
        Assert.Contains("عدد", hint);
        Assert.Contains("کوچک", hint);
        Assert.Contains("بزرگ", hint);
        Assert.Contains("نماد", hint);
        Assert.DoesNotContain("بزرگ", PasswordPolicyHint.Describe(new PasswordOptions { RequireUppercase = false }));
    }

    // ------------------------------------------------------------------ navigation

    [Fact]
    public void Sidebar_OffersMyAccount_ToAllThreeRoles()
    {
        var layout = File.ReadAllText(FindRepoFile("src", "ResidentialComplex.Web", "Components", "Layout", "MainLayout.razor"));

        var link = layout.IndexOf("<MudNavLink Href=\"/Account/Profile\"", StringComparison.OrdinalIgnoreCase);
        Assert.True(link > 0, "the sidebar (MudNavLink) must link to /Account/Profile");
        // the link sits inside an AuthorizeView that covers every role
        var viewStart = layout.LastIndexOf("<AuthorizeView", link, StringComparison.Ordinal);
        var opening = layout.Substring(viewStart, layout.IndexOf('>', viewStart) - viewStart);
        Assert.Contains("Administrator", opening);
        Assert.Contains("Worker", opening);
        Assert.Contains("Resident", opening);
    }

    private static string FindRepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ResidentialComplex.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }
}
