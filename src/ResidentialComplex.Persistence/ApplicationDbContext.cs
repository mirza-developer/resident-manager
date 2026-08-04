using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Persistence;

/// <summary>
/// Application database context with Identity support.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Apartment> Apartments => Set<Apartment>();
    public DbSet<House> Houses => Set<House>();
    public DbSet<FinancialItem> FinancialItems => Set<FinancialItem>();
    public DbSet<FinancialItemGroupPoint> FinancialItemGroupPoints => Set<FinancialItemGroupPoint>();
    public DbSet<MonthlyUsage> MonthlyUsages => Set<MonthlyUsage>();
    public DbSet<Bill> Bills => Set<Bill>();
    public DbSet<BillItem> BillItems => Set<BillItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}

/// <summary>
/// Application user extending ASP.NET Identity.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
}
