using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Persistence;
using ResidentialComplex.Web.Security;
using Xunit;

namespace ResidentialComplex.Tests;

/// <summary>
/// Resident sign-up against the real EF stores (ASP.NET Identity tables + Houses) on SQLite,
/// with a fake SMS provider: what the service decides must really end up in the database.
/// </summary>
public class ResidentRegistrationPersistenceTests : TestBase
{
    private const string Phone = "09123456789";

    private ResidentRegistrationService Service => GetService<ResidentRegistrationService>();
    private FakeSmsService Sms => GetService<FakeSmsService>();
    private UserManager<ApplicationUser> Users => GetService<UserManager<ApplicationUser>>();

    private async Task<House> AddHouseAsync(string phone = Phone, string? userId = null, string residentName = "علی رضایی")
    {
        var apartment = await GetService<IApartmentRepository>().AddAsync(new Apartment { Title = "برج" });
        return await GetService<IHouseRepository>().AddAsync(new House
        {
            Title = "واحد 1",
            ApartmentId = apartment.Id,
            ResidentName = residentName,
            ResidentPhoneNumber = phone,
            NumberOfResidents = 1,
            IsActive = true,
            ApplicationUserId = userId
        });
    }

    private async Task EnsureRolesAsync(params string[] roles)
    {
        var roleManager = GetService<RoleManager<IdentityRole>>();
        foreach (var role in roles)
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    private async Task<string> VerifiedTokenAsync()
    {
        var request = await Service.RequestOtpAsync(Phone);
        Assert.True(request.Success, request.Message);
        var verify = Service.VerifyOtp(Phone, Sms.LastCode);
        Assert.True(verify.Success, verify.Message);
        return verify.Token!;
    }

    private async Task<House> ReloadHouseAsync(int id)
    {
        Db.ChangeTracker.Clear();
        return await Db.Houses.AsNoTracking().SingleAsync(h => h.Id == id);
    }

    [Fact]
    public async Task SignUp_CreatesTheResidentUser_AndPersistsTheHouseLink()
    {
        await EnsureRolesAsync("Resident");
        var house = await AddHouseAsync();
        var token = await VerifiedTokenAsync();

        var result = await Service.SetPasswordAsync(Phone, token, "Secret123", "Secret123");

        Assert.True(result.Success, result.Message);
        Assert.True(result.CreatedNewUser);

        Db.ChangeTracker.Clear();
        var user = await Db.Users.AsNoTracking().SingleAsync();
        Assert.Equal(Phone, user.UserName);
        Assert.Equal("علی رضایی", user.FullName);
        Assert.Equal(user.Id, (await ReloadHouseAsync(house.Id)).ApplicationUserId);

        var stored = await Users.FindByNameAsync(Phone);
        Assert.NotNull(stored);
        Assert.True(await Users.IsInRoleAsync(stored!, "Resident"));
        Assert.True(await Users.CheckPasswordAsync(stored!, "Secret123"));
    }

    [Fact]
    public async Task SignUp_FindsTheHouse_WhateverFormatThePhoneIsStoredIn()
    {
        await EnsureRolesAsync("Resident");
        await AddHouseAsync("۰۹۱۲ ۳۴۵ ۶۷۸۹");   // Persian digits and spaces, as an admin may have typed it

        var result = await Service.RequestOtpAsync("+98 912 345 6789");

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { Phone }, Sms.SentTo);
    }

    [Fact]
    public async Task UnknownPhone_TouchesNothing()
    {
        await EnsureRolesAsync("Resident");
        await AddHouseAsync("09120000000");

        var result = await Service.RequestOtpAsync(Phone);

        Assert.Equal(RegistrationStatus.PhoneNotFound, result.Status);
        Assert.Empty(Sms.SentTo);
        Assert.Equal(0, await Db.Users.CountAsync());
    }

    [Fact]
    public async Task WithoutAVerifiedCode_NoUserIsCreated()
    {
        await EnsureRolesAsync("Resident");
        var house = await AddHouseAsync();
        await Service.RequestOtpAsync(Phone);   // code sent, never entered

        var result = await Service.SetPasswordAsync(Phone, "guessed-token", "Secret123", "Secret123");

        Assert.Equal(RegistrationStatus.TokenInvalid, result.Status);
        Assert.Equal(0, await Db.Users.CountAsync());
        Assert.Null((await ReloadHouseAsync(house.Id)).ApplicationUserId);
    }

    [Fact]
    public async Task ExistingResident_GetsANewPassword_InTheDatabase()
    {
        await EnsureRolesAsync("Resident");
        var existing = new ApplicationUser { UserName = Phone, FullName = "قدیمی" };
        Assert.True((await Users.CreateAsync(existing, "Old12345")).Succeeded);
        await Users.AddToRoleAsync(existing, "Resident");
        var house = await AddHouseAsync(userId: existing.Id);
        var token = await VerifiedTokenAsync();

        var result = await Service.SetPasswordAsync(Phone, token, "Brand9876", "Brand9876");

        Assert.True(result.Success, result.Message);
        Assert.False(result.CreatedNewUser);
        Db.ChangeTracker.Clear();
        Assert.Equal(1, await Db.Users.CountAsync());
        var stored = (await Users.FindByIdAsync(existing.Id))!;
        Assert.False(await Users.CheckPasswordAsync(stored, "Old12345"));
        Assert.True(await Users.CheckPasswordAsync(stored, "Brand9876"));
        Assert.Equal(existing.Id, (await ReloadHouseAsync(house.Id)).ApplicationUserId);
    }

    [Fact]
    public async Task AdministratorLinkedToAHouse_CannotBeTakenOver()
    {
        await EnsureRolesAsync("Resident", "Administrator");
        var admin = new ApplicationUser { UserName = "admin2", FullName = "مدیر" };
        Assert.True((await Users.CreateAsync(admin, "Old12345")).Succeeded);
        await Users.AddToRoleAsync(admin, "Administrator");
        await AddHouseAsync(userId: admin.Id);
        var token = await VerifiedTokenAsync();

        var result = await Service.SetPasswordAsync(Phone, token, "Hacked999", "Hacked999");

        Assert.Equal(RegistrationStatus.AccountNotAllowed, result.Status);
        Db.ChangeTracker.Clear();
        var stored = (await Users.FindByIdAsync(admin.Id))!;
        Assert.True(await Users.CheckPasswordAsync(stored, "Old12345"));
        Assert.False(await Users.CheckPasswordAsync(stored, "Hacked999"));
    }

    [Fact]
    public async Task WhenTheResidentRoleDoesNotExist_NoRoleLessAccountRemains()
    {
        // no "Resident" role row: the EF store throws on AddToRoleAsync
        var house = await AddHouseAsync();
        var token = await VerifiedTokenAsync();

        var result = await Service.SetPasswordAsync(Phone, token, "Secret123", "Secret123");

        Assert.Equal(RegistrationStatus.Error, result.Status);
        Db.ChangeTracker.Clear();
        Assert.Equal(0, await Db.Users.CountAsync());
        Assert.Null((await ReloadHouseAsync(house.Id)).ApplicationUserId);
    }

    [Fact]
    public async Task WeakPassword_IsRejected_AndNothingIsStored()
    {
        await EnsureRolesAsync("Resident");
        var house = await AddHouseAsync();
        var token = await VerifiedTokenAsync();

        var result = await Service.SetPasswordAsync(Phone, token, "abc", "abc");

        Assert.Equal(RegistrationStatus.PasswordRejected, result.Status);
        Assert.Equal(0, await Db.Users.CountAsync());
        Assert.Null((await ReloadHouseAsync(house.Id)).ApplicationUserId);
    }
}
