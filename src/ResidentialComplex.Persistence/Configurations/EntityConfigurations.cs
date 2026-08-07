using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Persistence.Configurations;

public class ApartmentConfiguration : IEntityTypeConfiguration<Apartment>
{
    public void Configure(EntityTypeBuilder<Apartment> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Title).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Description).HasMaxLength(500);
        builder.Property(a => a.RowVersion).IsConcurrencyToken();
    }
}

public class HouseConfiguration : IEntityTypeConfiguration<House>
{
    public void Configure(EntityTypeBuilder<House> builder)
    {
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Title).IsRequired().HasMaxLength(200);
        builder.Property(h => h.ResidentName).IsRequired().HasMaxLength(200);
        builder.Property(h => h.ResidentPhoneNumber).IsRequired().HasMaxLength(20);
        builder.Property(h => h.CurrentDebt).HasColumnType("decimal(18,2)");
        builder.Property(h => h.RowVersion).IsConcurrencyToken();
        builder.HasOne(h => h.Apartment).WithMany(a => a.Houses).HasForeignKey(h => h.ApartmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class FinancialItemConfiguration : IEntityTypeConfiguration<FinancialItem>
{
    public void Configure(EntityTypeBuilder<FinancialItem> builder)
    {
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Title).IsRequired().HasMaxLength(200);
        builder.Property(f => f.Description).HasMaxLength(500);
        builder.Property(f => f.TotalAmount).HasColumnType("decimal(18,2)");
        builder.Property(f => f.RowVersion).IsConcurrencyToken();
    }
}

public class FinancialItemGroupPointConfiguration : IEntityTypeConfiguration<FinancialItemGroupPoint>
{
    public void Configure(EntityTypeBuilder<FinancialItemGroupPoint> builder)
    {
        builder.HasKey(g => g.Id);
        builder.Property(g => g.PointValue).HasColumnType("decimal(18,2)");
        builder.HasOne(g => g.FinancialItem).WithMany(f => f.GroupPoints).HasForeignKey(g => g.FinancialItemId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class MonthlyUsageConfiguration : IEntityTypeConfiguration<MonthlyUsage>
{
    public void Configure(EntityTypeBuilder<MonthlyUsage> builder)
    {
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => new { m.HouseId, m.FinancialItemId, m.Year, m.Month }).IsUnique();
        builder.HasOne(m => m.House).WithMany(h => h.MonthlyUsages).HasForeignKey(m => m.HouseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(m => m.FinancialItem).WithMany().HasForeignKey(m => m.FinancialItemId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class BillConfiguration : IEntityTypeConfiguration<Bill>
{
    public void Configure(EntityTypeBuilder<Bill> builder)
    {
        builder.HasKey(b => b.Id);
        builder.HasIndex(b => new { b.HouseId, b.Year, b.Month }).IsUnique();
        builder.Property(b => b.TotalAmount).HasColumnType("decimal(18,2)");
        builder.Property(b => b.Description).HasMaxLength(500);
        builder.Property(b => b.RowVersion).IsConcurrencyToken();
        builder.HasOne(b => b.House).WithMany(h => h.Bills).HasForeignKey(b => b.HouseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class BillItemConfiguration : IEntityTypeConfiguration<BillItem>
{
    public void Configure(EntityTypeBuilder<BillItem> builder)
    {
        builder.HasKey(bi => bi.Id);
        builder.Property(bi => bi.Amount).HasColumnType("decimal(18,2)");
        builder.Property(bi => bi.Description).HasMaxLength(500);
        builder.HasOne(bi => bi.Bill).WithMany(b => b.BillItems).HasForeignKey(bi => bi.BillId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(bi => bi.FinancialItem).WithMany(f => f.BillItems).HasForeignKey(bi => bi.FinancialItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.HasOne(p => p.Bill).WithMany(b => b.Payments).HasForeignKey(p => p.BillId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.UserId).IsRequired().HasMaxLength(450);
        builder.Property(a => a.UserName).IsRequired().HasMaxLength(256);
        builder.Property(a => a.EntityName).IsRequired().HasMaxLength(100);
        builder.Property(a => a.EntityId).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Action).IsRequired().HasMaxLength(100);
    }
}
