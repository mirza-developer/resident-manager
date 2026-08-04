using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Application.Interfaces;

/// <summary>
/// Repository interface for audit log operations.
/// </summary>
public interface IAuditLogRepository
{
    Task<AuditLog> AddAsync(AuditLog log);
    Task<List<AuditLog>> GetAllAsync();
}
