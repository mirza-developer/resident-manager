using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Application.Interfaces;

/// <summary>
/// Repository interface for bill operations.
/// </summary>
public interface IBillRepository
{
    Task<List<Bill>> GetAllAsync();
    Task<List<Bill>> GetByHouseIdAsync(int houseId);
    Task<List<Bill>> GetByMonthYearAsync(int year, int month);
    Task<Bill?> GetByIdAsync(int id);
    Task<Bill?> GetByHouseMonthYearAsync(int houseId, int year, int month);
    Task<Bill> AddAsync(Bill bill);
    Task AddRangeAsync(IEnumerable<Bill> bills);
    Task UpdateAsync(Bill bill);
    Task DeleteAsync(int id);
    Task<List<Bill>> GetForReportAsync(int? year, int? month, int? houseId);

    /// <summary>
    /// Calculates the per-house amount for an EqualDivision financial item.
    /// </summary>
    decimal CalculateEqualDivisionAmount(decimal totalAmount, int houseCount);

    /// <summary>
    /// Calculates the Increasing Block Tariff (IBT) amount for the given house, financial item, and month.
    /// Fetches usage data from the database.
    /// </summary>
    Task<decimal> CalculateIbtAmountAsync(FinancialItem fi, int houseId, int year, int month);
}
