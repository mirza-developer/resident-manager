using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Application.Interfaces;

/// <summary>
/// Repository interface for monthly usage operations.
/// </summary>
public interface IMonthlyUsageRepository
{
    Task<List<MonthlyUsage>> GetByMonthYearAsync(int year, int month);
    Task<List<MonthlyUsage>> GetByFinancialItemMonthYearAsync(int financialItemId, int year, int month);
    Task<MonthlyUsage?> GetByHouseItemMonthYearAsync(int houseId, int financialItemId, int year, int month);
    Task<MonthlyUsage> AddAsync(MonthlyUsage usage);
    Task UpdateAsync(MonthlyUsage usage);
}
