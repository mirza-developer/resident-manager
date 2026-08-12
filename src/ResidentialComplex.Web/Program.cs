using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Application.Services;
using ResidentialComplex.Infrastructure.Services;
using ResidentialComplex.Infrastructure.Settings;
using ResidentialComplex.Persistence;
using ResidentialComplex.Persistence.Repositories;
using ResidentialComplex.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Database configuration
var dbProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connectionString = builder.Configuration["Database:ConnectionString"] ?? "Data Source=ResidentialComplex.db";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (dbProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        options.UseSqlServer(connectionString);
    else
        options.UseSqlite(connectionString);
});

// Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
});

// Repositories
builder.Services.AddScoped<IApartmentRepository, ApartmentRepository>();
builder.Services.AddScoped<IHouseRepository, HouseRepository>();
builder.Services.AddScoped<IFinancialItemRepository, FinancialItemRepository>();
builder.Services.AddScoped<IBillRepository, BillRepository>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IMonthlyUsageRepository, MonthlyUsageRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();

// Services
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<ReportService>();

// SMS Service
builder.Services.Configure<SmsOptions>(builder.Configuration.GetSection(SmsOptions.SectionName));
builder.Services.AddHttpClient(nameof(SmsService), (sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SmsOptions>>().Value;
    if (!string.IsNullOrEmpty(options.ApiBaseAddress))
        client.BaseAddress = new Uri(options.ApiBaseAddress);
});
builder.Services.AddScoped<ISmsService, SmsService>();

// Blazor
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
builder.Services.AddMudServices();

var app = builder.Build();

// Apply migrations and seed
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    // Seed roles
    foreach (var role in new[] { "Administrator", "Worker", "Resident" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    // Seed admin user
    const string adminUserName = "admin";
    if (await userManager.FindByNameAsync(adminUserName) == null)
    {
        var admin = new ApplicationUser
        {
            UserName = adminUserName,
            FullName = "مدیر سیستم"
        };
        var result = await userManager.CreateAsync(admin, "Admin123");
        if (result.Succeeded)
            await userManager.AddToRoleAsync(admin, "Administrator");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/Account/LoginPost", async (
    HttpContext httpContext,
    SignInManager<ApplicationUser> signInManager) =>
{
    var form = await httpContext.Request.ReadFormAsync();
    var userName = form["UserName"].ToString();
    var password = form["Password"].ToString();
    var rememberMe = form["RememberMe"].ToString() == "true";
    var returnUrl = form["ReturnUrl"].ToString();

    var result = await signInManager.PasswordSignInAsync(userName, password, rememberMe, lockoutOnFailure: false);

    if (result.Succeeded)
    {
        var redirect = !string.IsNullOrEmpty(returnUrl) ? returnUrl : "/";
        return Results.Redirect(redirect);
    }

    var errorRedirect = string.IsNullOrEmpty(returnUrl)
        ? "/Account/Login?error=%D9%86%D8%A7%D9%85+%DA%A9%D8%A7%D8%B1%D8%A8%D8%B1%DB%8C+%DB%8C%D8%A7+%D8%B1%D9%85%D8%B2+%D8%B9%D8%A8%D9%88%D8%B1+%D9%86%D8%A7%D8%AF%D8%B1%D8%B3%D8%AA+%D8%A7%D8%B3%D8%AA."
        : $"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}&error=%D9%86%D8%A7%D9%85+%DA%A9%D8%A7%D8%B1%D8%A8%D8%B1%DB%8C+%DB%8C%D8%A7+%D8%B1%D9%85%D8%B2+%D8%B9%D8%A8%D9%88%D8%B1+%D9%86%D8%A7%D8%AF%D8%B1%D8%B3%D8%AA+%D8%A7%D8%B3%D8%AA.";
    return Results.Redirect(errorRedirect);
});

app.MapGet("/Account/LogoutPost", async (
    HttpContext httpContext,
    SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/Account/Login");
});

app.Run();

/// <summary>
/// Partial class for WebApplicationFactory support in tests.
/// </summary>
public partial class Program { }
