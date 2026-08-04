namespace ResidentialComplex.Application.Interfaces;

/// <summary>
/// Service for writing audit log entries.
/// </summary>
public interface IAuditService
{
    Task LogAsync(string userId, string userName, string entityName, string entityId, string action, string? oldValues, string? newValues);
}
