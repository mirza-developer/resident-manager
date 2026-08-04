using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Application.Interfaces;

/// <summary>
/// Repository interface for house operations.
/// </summary>
public interface IHouseRepository
{
    Task<List<House>> GetAllAsync();
    Task<List<House>> GetByApartmentIdAsync(int apartmentId);
    Task<List<House>> GetActiveHousesAsync();
    Task<House?> GetByIdAsync(int id);
    Task<House?> GetByUserIdAsync(string userId);
    Task<House> AddAsync(House house);
    Task UpdateAsync(House house);
    Task DeleteAsync(int id);
}
