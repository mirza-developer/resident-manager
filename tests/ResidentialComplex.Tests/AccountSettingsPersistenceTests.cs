using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResidentialComplex.Persistence;
using ResidentialComplex.Web.Security;
using Xunit;

namespace ResidentialComplex.Tests;

/// <summary>"My account" against the real EF Identity stores (SQLite): what the service decides must reach the database.</summary>
public class AccountSettingsPersistenceTests : TestBase
{
    private AccountSettingsService Service => GetService<AccountSettingsService>();
    private UserManager<ApplicationUser> Users => GetService<UserManager<ApplicationUser>>();

    private async Task<ApplicationUser> AddUserAsync(string role, string userName = "someone")
    {
        await GetService<RoleManager<IdentityRole>>().CreateAsync(new IdentityRole(role));
        var user = new ApplicationUser { UserName = userName, FullName = "نام اولیه" };
        Assert.True((await Users.CreateAsync(user, "Old12345")).Succeeded);
        await Users.AddToRoleAsync(user, role);
        return user;
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Worker")]
    [InlineData("Resident")]
    public async Task PasswordChange_IsPersisted_ForEveryRole(string role)
    {
        var user = await AddUserAsync(role);

        var result = await Service.ChangePasswordAsync(user.Id, "Old12345", "Brand9876", "Brand9876");

        Assert.True(result.Success, result.Message);
        Db.ChangeTracker.Clear();
        var stored = (await Users.FindByIdAsync(user.Id))!;
        Assert.False(await Users.CheckPasswordAsync(stored, "Old12345"));
        Assert.True(await Users.CheckPasswordAsync(stored, "Brand9876"));
    }

    [Fact]
    public async Task ProfileUpdate_IsPersisted()
    {
        var user = await AddUserAsync("Resident");

        var result = await Service.UpdateProfileAsync(user.Id, "نام جدید");

        Assert.True(result.Success, result.Message);
        Db.ChangeTracker.Clear();
        Assert.Equal("نام جدید", (await Db.Users.AsNoTracking().SingleAsync()).FullName);
    }

    [Fact]
    public async Task WrongCurrentPasswords_AreCountedInTheDatabase_AndEventuallyLock()
    {
        var user = await AddUserAsync("Resident");

        for (var i = 0; i < 5; i++)
            Assert.False((await Service.ChangePasswordAsync(user.Id, $"Wrong{i}12345", "Brand9876", "Brand9876")).Success);

        Db.ChangeTracker.Clear();
        var stored = await Db.Users.AsNoTracking().SingleAsync();
        Assert.NotNull(stored.LockoutEnd);                       // locked by Identity's lockout
        var blocked = await Service.ChangePasswordAsync(user.Id, "Old12345", "Brand9876", "Brand9876");
        Assert.False(blocked.Success);
        Assert.Contains("مسدود", blocked.Message);
        Assert.True(await Users.CheckPasswordAsync((await Users.FindByIdAsync(user.Id))!, "Old12345"));
    }

    [Fact]
    public async Task OneUsersChange_DoesNotAffectAnother()
    {
        var first = await AddUserAsync("Resident", "first");
        var second = new ApplicationUser { UserName = "second", FullName = "دوم" };
        Assert.True((await Users.CreateAsync(second, "Other12345")).Succeeded);

        await Service.ChangePasswordAsync(first.Id, "Old12345", "Brand9876", "Brand9876");
        await Service.UpdateProfileAsync(first.Id, "نام جدید");

        Db.ChangeTracker.Clear();
        var stored = (await Users.FindByIdAsync(second.Id))!;
        Assert.True(await Users.CheckPasswordAsync(stored, "Other12345"));
        Assert.Equal("دوم", stored.FullName);
    }
}
