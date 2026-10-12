using AtmCash.Application.Abstractions.Persistence;
using AtmCash.Application.Abstractions.Services;
using AtmCash.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace AtmCash.Infrastructure.Persistence;

/// <summary>
/// Application lock on an arbitrary name using SQL Server's sp_getapplock, owned by the current transaction.
/// Used where there is no single row to lock (a card's daily limit spans quotes on many ATMs; an idempotency key may not exist yet).
/// </summary>
public sealed class SqlServerResourceLock : IResourceLock
{
    private const int TimeoutMilliseconds = 10_000;

    private readonly AppDbContext _context;

    public SqlServerResourceLock(AppDbContext context)
    {
        _context = context;
    }

    public async Task AcquireAsync(string resource, CancellationToken cancellationToken = default)
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Resource locks are owned by a transaction; start one first.");
        }

        var result = await _context.Database.SqlQuery<int>($"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {TimeoutMilliseconds};
            SELECT @result AS [Value];
            """).ToListAsync(cancellationToken);

        // 0 = granted, 1 = granted after waiting; negative = timeout / deadlock / error.
        if (result.Single() < 0)
        {
            throw new InvalidStateException(ErrorCodes.ConcurrencyConflict,
                "The resource is busy with another request. Please retry.");
        }
    }
}

public sealed class IdempotencyStore : IIdempotencyStore
{
    private readonly AppDbContext _context;
    private readonly IResourceLock _resourceLock;

    public IdempotencyStore(AppDbContext context, IResourceLock resourceLock)
    {
        _context = context;
        _resourceLock = resourceLock;
    }

    public Task LockAsync(string key, CancellationToken cancellationToken = default) =>
        _resourceLock.AcquireAsync($"idempotency:{key}", cancellationToken);

    public Task<IdempotencyRecord?> FindAsync(string key, CancellationToken cancellationToken = default) =>
        _context.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(r => r.Key == key, cancellationToken);

    public async Task AddAsync(IdempotencyRecord record, CancellationToken cancellationToken = default) =>
        await _context.IdempotencyRecords.AddAsync(record, cancellationToken);
}
