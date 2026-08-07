using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Application.Interfaces;

/// <summary>
/// Repository interface for apartment operations.
/// </summary>
public interface IApartmentRepository
{
    Task<List<Apartment>> GetAllAsync();
    Task<Apartment?> GetByIdAsync(int id);
    Task<Apartment> AddAsync(Apartment apartment);
    Task UpdateAsync(Apartment apartment);
    Task DeleteAsync(int id);
}
