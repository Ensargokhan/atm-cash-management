using System.Linq.Expressions;

namespace AtmCash.Application.Abstractions.Persistence;

/// <summary>Generic repository for aggregate roots. Changes are saved through <see cref="IUnitOfWork"/>.</summary>
public interface IRepository<TEntity> where TEntity : class
{
    Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TEntity>> ListAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    void Update(TEntity entity);

    void Remove(TEntity entity);
}

public interface IUnitOfWork
{
    bool HasActiveTransaction { get; }

    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists tracked changes. Ledger movements and domain events collected by aggregates are
    /// written in the same call, so balances, ledger and outbox can never diverge.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
