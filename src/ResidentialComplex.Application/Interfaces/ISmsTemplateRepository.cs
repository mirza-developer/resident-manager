using ResidentialComplex.Domain.Entities;

namespace ResidentialComplex.Application.Interfaces;

/// <summary>
/// Repository interface for admin-configurable SMS templates.
/// </summary>
public interface ISmsTemplateRepository
{
    Task<List<SmsTemplate>> GetAllAsync();
    Task<SmsTemplate?> GetByIdAsync(int id);

    /// <summary>Looks up a template by its stable key (see SmsTemplateKeys), not its editable Title.</summary>
    Task<SmsTemplate?> GetByKeyAsync(string key);

    Task<SmsTemplate> AddAsync(SmsTemplate template);
    Task UpdateAsync(SmsTemplate template);
}
