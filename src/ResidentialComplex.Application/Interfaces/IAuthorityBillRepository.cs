using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Application.Interfaces;

/// <summary>
/// Repository interface for authority (bulk) bills.
/// </summary>
public interface IAuthorityBillRepository
{
    Task<List<AuthorityBill>> GetAllAsync();
    Task<AuthorityBill?> GetByIdAsync(int id);
    Task<AuthorityBill?> GetByItemMonthYearAsync(int financialItemId, int year, int month);
    Task<AuthorityBill> AddAsync(AuthorityBill bill);
    Task UpdateAsync(AuthorityBill bill);
    Task DeleteAsync(int id);
}
