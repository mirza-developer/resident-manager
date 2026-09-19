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
    /// Calculates the Whole-Consumption Bracket Pricing amount for the given house, financial item, and month.
    /// The house's total usage is matched to the single tier/bracket it falls into, and the ENTIRE usage
    /// is billed at that bracket's rate (no splitting across brackets, unlike Incremental Block Tariff / IBT).
    /// Fetches usage data from the database.
    /// </summary>
    Task<decimal> CalculateBracketAmountAsync(FinancialItem fi, int houseId, int year, int month);
}
