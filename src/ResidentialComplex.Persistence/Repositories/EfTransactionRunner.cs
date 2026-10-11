using ResidentialComplex.Application.Interfaces;

namespace ResidentialComplex.Persistence.Repositories;

/// <summary>
/// Runs a unit of work in one database transaction on the shared (scoped) DbContext.
/// Every SaveChanges done by the repositories inside <c>work</c> is committed together or rolled back.
/// If a transaction is already open the work simply joins it.
/// </summary>
public class EfTransactionRunner : ITransactionRunner
{
    private readonly ApplicationDbContext _db;
    public EfTransactionRunner(ApplicationDbContext db) => _db = db;

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> work)
    {
        if (_db.Database.CurrentTransaction is not null)
            return await work();

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var result = await work();
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
