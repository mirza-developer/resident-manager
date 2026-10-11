using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Application.Services;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Domain.Enums;
using ResidentialComplex.Persistence;
using Xunit;

namespace ResidentialComplex.Tests;

/// <summary>
/// Boots the real web app (Program.cs) on a throw-away SQLite file with a fake Zibal, so the
/// callback endpoint, cookie settings and login redirect can be tested end to end.
/// </summary>
public class PaymentWebFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rc-payment-tests-{Guid.NewGuid():N}.db");

    public FakePaymentGateway Gateway { get; } = new();

    public PaymentWebFactory()
    {
        // Program.cs reads configuration eagerly, so use environment variables (always visible to it).
        Environment.SetEnvironmentVariable("Database__Provider", "Sqlite");
        Environment.SetEnvironmentVariable("Database__ConnectionString", $"Data Source={_dbPath}");
        Environment.SetEnvironmentVariable("Zibal__Merchant", "test-merchant");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(Gateway);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            SqliteConnectionPoolCleaner.ClearAll();
            File.Delete(_dbPath);
        }
        catch
        {
            // temp file; the OS cleans it up eventually
        }
    }
}

internal static class SqliteConnectionPoolCleaner
{
    public static void ClearAll() => Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
}

public class PaymentEndpointTests : IClassFixture<PaymentWebFactory>
{
    private readonly PaymentWebFactory _factory;

    public PaymentEndpointTests(PaymentWebFactory factory) => _factory = factory;

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<(int BillId, int HouseId, Guid PublicId, long TrackId, string OrderId)> SeedPaidSessionAsync(string title)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var apartment = new Apartment { Title = $"برج {title}" };
        db.Apartments.Add(apartment);
        await db.SaveChangesAsync();
        var house = new House { Title = title, ApartmentId = apartment.Id, ResidentName = "ساکن", ResidentPhoneNumber = "09123456789", IsActive = true, ApplicationUserId = $"owner-{title}", CurrentDebt = 500_000m };
        db.Houses.Add(house);
        await db.SaveChangesAsync();
        var bill = new Bill { HouseId = house.Id, Year = 1404, Month = 1, TotalAmount = 500_000m, Status = BillStatus.Approved, CreatedDate = DateTime.Now };
        db.Bills.Add(bill);
        await db.SaveChangesAsync();

        var payments = scope.ServiceProvider.GetRequiredService<PaymentService>();
        var start = await payments.StartPaymentAsync(bill.Id, house.ApplicationUserId!, "owner", "https://localhost/payment/callback");
        Assert.True(start.Success, start.ErrorMessage);

        var attempt = (await scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>().GetByPublicIdAsync(start.AttemptPublicId!.Value))!;
        _factory.Gateway.Pay(attempt.TrackId!.Value);
        return (bill.Id, house.Id, attempt.PublicId, attempt.TrackId!.Value, attempt.OrderId);
    }

    [Fact]
    public async Task Callback_WithoutTrackId_RedirectsToTheGenericResultPage()
    {
        var response = await NewClient().GetAsync("/payment/callback");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/payments/result", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Callback_WithUnknownTrackId_RedirectsWithoutRevealingAnything()
    {
        var response = await NewClient().GetAsync("/payment/callback?trackId=999999999&success=1&status=2&orderId=x");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/payments/result", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Callback_IsAnonymous_VerifiesWithTheGateway_AndSettlesTheBill()
    {
        var (billId, houseId, publicId, track, orderId) = await SeedPaidSessionAsync("callback-ok");

        // No cookie / no login: the payment must still be verified and credited.
        var response = await NewClient().GetAsync($"/payment/callback?trackId={track}&success=1&status=2&orderId={orderId}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/payments/result/{publicId}", response.Headers.Location!.OriginalString);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(BillStatus.Paid, (await db.Bills.AsNoTracking().SingleAsync(b => b.Id == billId)).Status);
        Assert.Equal(0m, (await db.Houses.AsNoTracking().SingleAsync(h => h.Id == houseId)).CurrentDebt);
        Assert.Equal(PaymentAttemptStatus.Succeeded, (await db.PaymentAttempts.AsNoTracking().SingleAsync(a => a.PublicId == publicId)).Status);
    }

    [Fact]
    public async Task Callback_Replay_RedirectsToTheSameResult_AndDoesNotCreditTwice()
    {
        var (billId, _, publicId, track, orderId) = await SeedPaidSessionAsync("callback-replay");
        var client = NewClient();
        var url = $"/payment/callback?trackId={track}&success=1&status=2&orderId={orderId}";

        var first = await client.GetAsync(url);
        var second = await client.GetAsync(url);
        var forged = await client.GetAsync($"/payment/callback?trackId={track}&success=0&status=3&orderId=attacker");

        Assert.Equal($"/payments/result/{publicId}", first.Headers.Location!.OriginalString);
        Assert.Equal($"/payments/result/{publicId}", second.Headers.Location!.OriginalString);
        Assert.Equal($"/payments/result/{publicId}", forged.Headers.Location!.OriginalString);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Payments.CountAsync(p => p.BillId == billId));
    }

    [Fact]
    public void ApplicationCookie_IsSameSiteLax_SoTheSessionSurvivesTheBankRoundTrip()
    {
        var options = _factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
        Assert.True(options.Cookie.HttpOnly);
    }

    [Theory]
    [InlineData("https://evil.example/steal", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/payments/result/abc", "/payments/result/abc")]
    [InlineData("http://localhost/payments/result/xyz", "/payments/result/xyz")]
    public async Task Login_RedirectsOnlyToLocalReturnUrls(string returnUrl, string expectedLocation)
    {
        var response = await NewClient().PostAsync("/Account/LoginPost", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = "admin",
            ["Password"] = "Admin123",
            ["ReturnUrl"] = returnUrl
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expectedLocation, response.Headers.Location!.OriginalString);
    }
}
