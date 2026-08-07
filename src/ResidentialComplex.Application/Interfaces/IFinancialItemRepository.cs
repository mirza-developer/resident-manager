using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Application.Interfaces;

/// <summary>
/// Repository interface for financial item operations.
/// </summary>
public interface IFinancialItemRepository
{
    Task<List<FinancialItem>> GetAllAsync();
    Task<List<FinancialItem>> GetActiveAsync();
    Task<FinancialItem?> GetByIdAsync(int id);
    Task<FinancialItem> AddAsync(FinancialItem item);
    Task UpdateAsync(FinancialItem item);
    Task DeleteAsync(int id);
}
