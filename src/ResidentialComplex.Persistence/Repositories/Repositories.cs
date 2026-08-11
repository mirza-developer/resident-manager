using Microsoft.EntityFrameworkCore;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Persistence.Repositories;

public class ApartmentRepository : IApartmentRepository
{
    private readonly ApplicationDbContext _db;
    public ApartmentRepository(ApplicationDbContext db) => _db = db;

    public async Task<List<Apartment>> GetAllAsync() => await _db.Apartments.Include(a => a.Houses).ToListAsync();
    public async Task<Apartment?> GetByIdAsync(int id) => await _db.Apartments.Include(a => a.Houses).FirstOrDefaultAsync(a => a.Id == id);
    public async Task<Apartment> AddAsync(Apartment apartment) { _db.Apartments.Add(apartment); await _db.SaveChangesAsync(); return apartment; }
    public async Task UpdateAsync(Apartment apartment) { _db.Apartments.Update(apartment); await _db.SaveChangesAsync(); }
    public async Task DeleteAsync(int id) { var e = await _db.Apartments.FindAsync(id); if (e != null) { _db.Apartments.Remove(e); await _db.SaveChangesAsync(); } }
}

public class HouseRepository : IHouseRepository
{
    private readonly ApplicationDbContext _db;
    public HouseRepository(ApplicationDbContext db) => _db = db;

    public async Task<List<House>> GetAllAsync() => await _db.Houses.Include(h => h.Apartment).ToListAsync();
    public async Task<List<House>> GetByApartmentIdAsync(int apartmentId) => await _db.Houses.Where(h => h.ApartmentId == apartmentId).ToListAsync();
    public async Task<List<House>> GetActiveHousesAsync() => await _db.Houses.Where(h => h.IsActive).ToListAsync();
    public async Task<House?> GetByIdAsync(int id) => await _db.Houses.Include(h => h.Apartment).FirstOrDefaultAsync(h => h.Id == id);
    public async Task<House?> GetByUserIdAsync(string userId) => await _db.Houses.Include(h => h.Apartment).FirstOrDefaultAsync(h => h.ApplicationUserId == userId);
    public async Task<House> AddAsync(House house) { _db.Houses.Add(house); await _db.SaveChangesAsync(); return house; }
    public async Task UpdateAsync(House house) { _db.Houses.Update(house); await _db.SaveChangesAsync(); }
    public async Task DeleteAsync(int id) { var e = await _db.Houses.FindAsync(id); if (e != null) { _db.Houses.Remove(e); await _db.SaveChangesAsync(); } }
}

public class FinancialItemRepository : IFinancialItemRepository
{
    private readonly ApplicationDbContext _db;
    public FinancialItemRepository(ApplicationDbContext db) => _db = db;

    public async Task<List<FinancialItem>> GetAllAsync() => await _db.FinancialItems.Include(f => f.Tiers).ToListAsync();
    public async Task<List<FinancialItem>> GetActiveAsync() => await _db.FinancialItems.Include(f => f.Tiers).Where(f => f.IsActive).ToListAsync();
    public async Task<FinancialItem?> GetByIdAsync(int id) => await _db.FinancialItems.Include(f => f.Tiers).FirstOrDefaultAsync(f => f.Id == id);
    public async Task<FinancialItem> AddAsync(FinancialItem item) { _db.FinancialItems.Add(item); await _db.SaveChangesAsync(); return item; }
    public async Task UpdateAsync(FinancialItem item) { _db.FinancialItems.Update(item); await _db.SaveChangesAsync(); }
    public async Task DeleteAsync(int id) { var e = await _db.FinancialItems.FindAsync(id); if (e != null) { _db.FinancialItems.Remove(e); await _db.SaveChangesAsync(); } }

    public async Task<List<FinancialItemTier>> GetTiersAsync(int financialItemId) =>
        await _db.FinancialItemTiers.Where(t => t.FinancialItemId == financialItemId).OrderBy(t => t.TierOrder).ToListAsync();

    public async Task<FinancialItemTier> AddTierAsync(FinancialItemTier tier) { _db.FinancialItemTiers.Add(tier); await _db.SaveChangesAsync(); return tier; }

    public async Task DeleteTierAsync(int tierId) { var t = await _db.FinancialItemTiers.FindAsync(tierId); if (t != null) { _db.FinancialItemTiers.Remove(t); await _db.SaveChangesAsync(); } }
}

public class BillRepository : IBillRepository
{
    private readonly ApplicationDbContext _db;
    public BillRepository(ApplicationDbContext db) => _db = db;

    public async Task<List<Bill>> GetAllAsync() => await _db.Bills.Include(b => b.BillItems).Include(b => b.House).ToListAsync();
    public async Task<List<Bill>> GetByHouseIdAsync(int houseId) => await _db.Bills.Include(b => b.BillItems).ThenInclude(bi => bi.FinancialItem).ThenInclude(fi => fi.Tiers).Include(b => b.Payments).Where(b => b.HouseId == houseId).OrderByDescending(b => b.Year).ThenByDescending(b => b.Month).ToListAsync();
    public async Task<List<Bill>> GetByMonthYearAsync(int year, int month) => await _db.Bills.Include(b => b.BillItems).ThenInclude(bi => bi.FinancialItem).ThenInclude(fi => fi.Tiers).Include(b => b.House).Where(b => b.Year == year && b.Month == month).ToListAsync();
    public async Task<Bill?> GetByIdAsync(int id) => await _db.Bills.Include(b => b.BillItems).ThenInclude(bi => bi.FinancialItem).ThenInclude(fi => fi.Tiers).Include(b => b.House).Include(b => b.Payments).FirstOrDefaultAsync(b => b.Id == id);
    public async Task<Bill?> GetByHouseMonthYearAsync(int houseId, int year, int month) => await _db.Bills.FirstOrDefaultAsync(b => b.HouseId == houseId && b.Year == year && b.Month == month);
    public async Task<Bill> AddAsync(Bill bill) { _db.Bills.Add(bill); await _db.SaveChangesAsync(); return bill; }
    public async Task AddRangeAsync(IEnumerable<Bill> bills) { _db.Bills.AddRange(bills); await _db.SaveChangesAsync(); }
    public async Task UpdateAsync(Bill bill) { _db.Bills.Update(bill); await _db.SaveChangesAsync(); }
    public async Task DeleteAsync(int id) { var e = await _db.Bills.Include(b => b.BillItems).FirstOrDefaultAsync(b => b.Id == id); if (e != null) { _db.Bills.Remove(e); await _db.SaveChangesAsync(); } }
    public async Task<List<Bill>> GetForReportAsync(int? year, int? month, int? houseId)
    {
        var query = _db.Bills.Include(b => b.House).AsQueryable();
        if (year.HasValue) query = query.Where(b => b.Year == year.Value);
        if (month.HasValue) query = query.Where(b => b.Month == month.Value);
        if (houseId.HasValue) query = query.Where(b => b.HouseId == houseId.Value);
        return await query.ToListAsync();
    }

    public decimal CalculateEqualDivisionAmount(decimal totalAmount, int houseCount)
    {
        if (houseCount <= 0)
        {
            return 0m;
        }

        return Math.Round(totalAmount / houseCount, 0);
    }

    public async Task<decimal> CalculateIbtAmountAsync(FinancialItem fi, int houseId, int year, int month)
    {
        var tiers = fi.Tiers.OrderBy(t => t.TierOrder).ToList();
        if (tiers.Count == 0)
            return 0m;

        var usageRecord = await _db.MonthlyUsages
            .FirstOrDefaultAsync(m => m.HouseId == houseId && m.FinancialItemId == fi.Id && m.Year == year && m.Month == month);

        int usage = usageRecord?.UsageCount ?? 0;
        if (usage <= 0)
            return 0m;

        decimal total = 0m;
        int consumed = 0;
        long previousLimit = 0;

        foreach (var tier in tiers)
        {
            if (consumed >= usage)
                break;

            if (!tier.UpperLimit.HasValue)
            {
                total += (usage - consumed) * tier.RatePerUnit;
                consumed = usage;
                break;
            }

            long blockEnd = (long)tier.UpperLimit.Value;
            long blockSize = blockEnd - previousLimit;

            int unitsInBlock = (int)Math.Min(usage - consumed, blockSize);
            total += unitsInBlock * tier.RatePerUnit;
            consumed += unitsInBlock;
            previousLimit = blockEnd;
        }

        return Math.Round(total, 0);
    }
}

public class PaymentRepository : IPaymentRepository
{
    private readonly ApplicationDbContext _db;
    public PaymentRepository(ApplicationDbContext db) => _db = db;

    public async Task<List<Payment>> GetAllAsync() => await _db.Payments.Include(p => p.Bill).ToListAsync();
    public async Task<List<Payment>> GetByBillIdAsync(int billId) => await _db.Payments.Where(p => p.BillId == billId).ToListAsync();
    public async Task<Payment?> GetByIdAsync(int id) => await _db.Payments.FindAsync(id);
    public async Task<Payment> AddAsync(Payment payment) { _db.Payments.Add(payment); await _db.SaveChangesAsync(); return payment; }
}

public class MonthlyUsageRepository : IMonthlyUsageRepository
{
    private readonly ApplicationDbContext _db;
    public MonthlyUsageRepository(ApplicationDbContext db) => _db = db;

    public async Task<List<MonthlyUsage>> GetByMonthYearAsync(int year, int month) => await _db.MonthlyUsages.Include(m => m.House).Include(m => m.FinancialItem).Where(m => m.Year == year && m.Month == month).ToListAsync();
    public async Task<List<MonthlyUsage>> GetByFinancialItemMonthYearAsync(int financialItemId, int year, int month) => await _db.MonthlyUsages.Include(m => m.House).Where(m => m.FinancialItemId == financialItemId && m.Year == year && m.Month == month).ToListAsync();
    public async Task<MonthlyUsage?> GetByHouseItemMonthYearAsync(int houseId, int financialItemId, int year, int month) => await _db.MonthlyUsages.FirstOrDefaultAsync(m => m.HouseId == houseId && m.FinancialItemId == financialItemId && m.Year == year && m.Month == month);
    public async Task<MonthlyUsage> AddAsync(MonthlyUsage usage) { _db.MonthlyUsages.Add(usage); await _db.SaveChangesAsync(); return usage; }
    public async Task UpdateAsync(MonthlyUsage usage) { _db.MonthlyUsages.Update(usage); await _db.SaveChangesAsync(); }
}

public class AuditLogRepository : IAuditLogRepository
{
    private readonly ApplicationDbContext _db;
    public AuditLogRepository(ApplicationDbContext db) => _db = db;

    public async Task<AuditLog> AddAsync(AuditLog log) { _db.AuditLogs.Add(log); await _db.SaveChangesAsync(); return log; }
    public async Task<List<AuditLog>> GetAllAsync() => await _db.AuditLogs.OrderByDescending(a => a.DateTime).ToListAsync();
}
