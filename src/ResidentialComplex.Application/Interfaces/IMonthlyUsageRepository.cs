using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Application.Interfaces;

/// <summary>
/// Repository interface for monthly usage operations.
/// </summary>
public interface IMonthlyUsageRepository
{
    Task<List<MonthlyUsage>> GetByMonthYearAsync(int year, int month);
    Task<MonthlyUsage?> GetByHouseMonthYearAsync(int houseId, int year, int month);
    Task<MonthlyUsage> AddAsync(MonthlyUsage usage);
    Task UpdateAsync(MonthlyUsage usage);
}
