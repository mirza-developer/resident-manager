using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Application.Interfaces;

/// <summary>
/// Outcome of creating resident login accounts for houses.
/// </summary>
public record ResidentAccountsResult(int CreatedUsers, int LinkedHouses, int SkippedHouses);

/// <summary>
/// Repository interface for house operations.
/// </summary>
public interface IHouseRepository
{
    /// <summary>
    /// For houses without a linked user, finds or creates a user (phone number as username) and links it.
    /// Houses that already have a user, and existing users, are never modified.
    /// </summary>
    Task<ResidentAccountsResult> CreateResidentAccountsAsync(string defaultPassword, string role);

    Task<List<House>> GetAllAsync();
    Task<List<House>> GetByApartmentIdAsync(int apartmentId);
    Task<List<House>> GetActiveHousesAsync();
    Task<House?> GetByIdAsync(int id);
    Task<House?> GetByUserIdAsync(string userId);
    Task<House> AddAsync(House house);
    Task UpdateAsync(House house);
    Task DeleteAsync(int id);
}
