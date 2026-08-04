using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Infrastructure.Services;

/// <summary>
/// Audit service implementation that logs financial modifications.
/// </summary>
public class AuditService : IAuditService
{
    private readonly IAuditLogRepository _repo;

    public AuditService(IAuditLogRepository repo) => _repo = repo;

    public async Task LogAsync(string userId, string userName, string entityName, string entityId, string action, string? oldValues, string? newValues)
    {
        await _repo.AddAsync(new AuditLog
        {
            UserId = userId,
            UserName = userName,
            DateTime = DateTime.UtcNow,
            EntityName = entityName,
            EntityId = entityId,
            Action = action,
            OldValues = oldValues,
            NewValues = newValues
        });
    }
}
