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
}
