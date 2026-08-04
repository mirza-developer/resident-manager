using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Application.Interfaces;

/// <summary>
/// Repository interface for payment operations.
/// </summary>
public interface IPaymentRepository
{
    Task<List<Payment>> GetAllAsync();
    Task<List<Payment>> GetByBillIdAsync(int billId);
    Task<Payment?> GetByIdAsync(int id);
    Task<Payment> AddAsync(Payment payment);
}
