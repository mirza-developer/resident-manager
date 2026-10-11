namespace ResidentialComplex.Application.Interfaces;

/// <summary>
/// Runs several repository calls as one atomic database transaction
/// (all changes are committed together or rolled back together).
/// </summary>
public interface ITransactionRunner
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> work);
}
