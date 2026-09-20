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

    /// <summary>
    /// Returns the most recent usage record strictly before the given year/month for this
    /// house and financial item (by Year/Month, not by date the row was created), or null
    /// if none exists. Used to carry a month's CurrentReading forward as next month's
    /// PreviousReading.
    /// </summary>
    Task<MonthlyUsage?> GetLatestBeforeAsync(int houseId, int financialItemId, int year, int month);

    Task<MonthlyUsage> AddAsync(MonthlyUsage usage);
    Task UpdateAsync(MonthlyUsage usage);
}
