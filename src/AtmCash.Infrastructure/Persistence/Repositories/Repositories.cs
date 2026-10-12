using System.Linq.Expressions;
using AtmCash.Application.Abstractions.Persistence;
using AtmCash.Domain.Atms;
using AtmCash.Domain.Common;
using AtmCash.Domain.ExchangeRates;
using AtmCash.Domain.Ledger;
using AtmCash.Domain.Reconciliations;
using AtmCash.Domain.Withdrawals;
using Microsoft.EntityFrameworkCore;

namespace AtmCash.Infrastructure.Persistence.Repositories;

public class Repository<TEntity> : IRepository<TEntity> where TEntity : class
{
    public Repository(AppDbContext context)
    {
        Context = context;
    }

    protected AppDbContext Context { get; }

    protected DbSet<TEntity> Set => Context.Set<TEntity>();

    public virtual async Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default) =>
        await Set.FindAsync(new[] { id }, cancellationToken);

    public virtual async Task<IReadOnlyList<TEntity>> ListAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default) =>
        predicate is null
            ? await Set.ToListAsync(cancellationToken)
            : await Set.Where(predicate).ToListAsync(cancellationToken);

    public virtual Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) =>
        Set.AnyAsync(predicate, cancellationToken);

    public virtual async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default) =>
        await Set.AddAsync(entity, cancellationToken);

    public virtual void Update(TEntity entity) => Set.Update(entity);

    public virtual void Remove(TEntity entity) => Set.Remove(entity);
}

public sealed class AtmRepository : Repository<Atm>, IAtmRepository
{
    public AtmRepository(AppDbContext context) : base(context)
    {
    }

    public override async Task<Atm?> GetByIdAsync(object id, CancellationToken cancellationToken = default) =>
        await GetWithCassettesAsync((Guid)id, cancellationToken);

    public Task<Atm?> GetWithCassettesAsync(Guid atmId, CancellationToken cancellationToken = default) =>
        Set.Include(a => a.Cassettes).SingleOrDefaultAsync(a => a.Id == atmId, cancellationToken);

    public async Task<Atm?> GetForUpdateAsync(Guid atmId, CancellationToken cancellationToken = default)
    {
        if (Context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("GetForUpdateAsync must run inside a transaction, otherwise the lock is released immediately.");
        }

        // UPDLOCK: exclusive-for-writers lock on the ATM row, held until commit/rollback.
        // A second request for the same ATM waits here, then reads the committed balances and reservations.
        return await Set
            .FromSqlInterpolated($"SELECT * FROM [Atms] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {atmId}")
            .Include(a => a.Cassettes)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Atm>> ListWithCassettesAsync(Guid? atmId = null, CancellationToken cancellationToken = default)
    {
        var query = Set.AsNoTracking().Include(a => a.Cassettes).AsSplitQuery();
        if (atmId is not null)
        {
            query = query.Where(a => a.Id == atmId);
        }

        return await query.OrderBy(a => a.Code).ToListAsync(cancellationToken);
    }

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default) =>
        Set.AnyAsync(a => a.Code == code, cancellationToken);
}

public sealed class QuoteRepository : Repository<Quote>, IQuoteRepository
{
    public QuoteRepository(AppDbContext context) : base(context)
    {
    }

    public Task<Guid?> GetAtmIdAsync(Guid quoteId, CancellationToken cancellationToken = default) =>
        Set.Where(q => q.Id == quoteId).Select(q => (Guid?)q.AtmId).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Quote>> ListPendingByAtmAsync(Guid atmId, CancellationToken cancellationToken = default) =>
        await Set.Where(q => q.AtmId == atmId && q.Status == QuoteStatus.Pending).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListAtmIdsWithExpiredQuotesAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
        await Set.Where(q => q.Status == QuoteStatus.Pending && q.ExpiresAt <= now)
            .Select(q => q.AtmId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<int> GetUsedDailyAmountAsync(string cardHash, Currency currency, DateTimeOffset dayStart, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var cardQuotes = Set.Where(q => q.CardHash == cardHash && q.Currency == currency);

        var confirmed = await cardQuotes
            .Where(q => q.Status == QuoteStatus.Confirmed && q.ClosedAt >= dayStart)
            .SumAsync(q => q.DispensedAmount ?? 0, cancellationToken);

        var reserved = await cardQuotes
            .Where(q => q.Status == QuoteStatus.Pending && q.ExpiresAt > now)
            .SumAsync(q => q.ReservedAmount, cancellationToken);

        return confirmed + reserved;
    }
}

public sealed class ReconciliationRepository : Repository<Reconciliation>, IReconciliationRepository
{
    public ReconciliationRepository(AppDbContext context) : base(context)
    {
    }

    public Task<Guid?> GetAtmIdAsync(Guid reconciliationId, CancellationToken cancellationToken = default) =>
        Set.Where(r => r.Id == reconciliationId).Select(r => (Guid?)r.AtmId).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Reconciliation>> ListByAtmAsync(Guid atmId, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().Where(r => r.AtmId == atmId).OrderByDescending(r => r.SubmittedAt).ToListAsync(cancellationToken);
}

public sealed class ExchangeRateRepository : Repository<ExchangeRate>, IExchangeRateRepository
{
    public ExchangeRateRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyDictionary<Currency, decimal>> GetRatesToTryAsync(CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().ToDictionaryAsync(r => r.Currency, r => r.RateToTry, cancellationToken);
}

public sealed class MovementRepository : IMovementRepository
{
    private readonly AppDbContext _context;

    public MovementRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<CassetteMovement>> ListByAtmAsync(Guid atmId, int? slot, MovementType? type, int take, CancellationToken cancellationToken = default)
    {
        var query = _context.Movements.AsNoTracking().Where(m => m.AtmId == atmId);
        if (slot is not null)
        {
            query = query.Where(m => m.Slot == slot);
        }

        if (type is not null)
        {
            query = query.Where(m => m.Type == type);
        }

        return await query.OrderByDescending(m => m.CreatedAt).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LedgerBalance>> GetLedgerBalancesAsync(Guid? atmId, CancellationToken cancellationToken = default)
    {
        var query = _context.Movements.AsNoTracking();
        if (atmId is not null)
        {
            query = query.Where(m => m.AtmId == atmId);
        }

        return await query
            .GroupBy(m => new { m.AtmId, m.CassetteId, m.Currency, m.Denomination })
            .Select(g => new LedgerBalance(
                g.Key.AtmId,
                g.Key.CassetteId,
                g.Key.Currency,
                g.Key.Denomination,
                g.Sum(m => m.CountDelta),
                g.Sum(m => m.ReservedDelta)))
            .ToListAsync(cancellationToken);
    }
}
