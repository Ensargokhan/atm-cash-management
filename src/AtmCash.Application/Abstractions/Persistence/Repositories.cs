using AtmCash.Domain.Atms;
using AtmCash.Domain.Common;
using AtmCash.Domain.ExchangeRates;
using AtmCash.Domain.Ledger;
using AtmCash.Domain.Reconciliations;
using AtmCash.Domain.Withdrawals;

namespace AtmCash.Application.Abstractions.Persistence;

public interface IAtmRepository : IRepository<Atm>
{
    /// <summary>Loads the ATM with its cassettes for reading.</summary>
    Task<Atm?> GetWithCassettesAsync(Guid atmId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the ATM with its cassettes and takes an exclusive row lock on it until the transaction ends.
    /// Every operation that changes an ATM's cash goes through this, so operations on the same ATM are serialised.
    /// </summary>
    Task<Atm?> GetForUpdateAsync(Guid atmId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Atm>> ListWithCassettesAsync(Guid? atmId = null, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);
}

public interface IQuoteRepository : IRepository<Quote>
{
    Task<Guid?> GetAtmIdAsync(Guid quoteId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Quote>> ListPendingByAtmAsync(Guid atmId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> ListAtmIdsWithExpiredQuotesAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Amount that counts towards the card's daily limit: confirmed withdrawals since <paramref name="dayStart"/>
    /// plus pending (not yet expired) quotes.
    /// </summary>
    Task<int> GetUsedDailyAmountAsync(string cardHash, Currency currency, DateTimeOffset dayStart, DateTimeOffset now, CancellationToken cancellationToken = default);
}

public interface IReconciliationRepository : IRepository<Reconciliation>
{
    Task<Guid?> GetAtmIdAsync(Guid reconciliationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Reconciliation>> ListByAtmAsync(Guid atmId, CancellationToken cancellationToken = default);
}

public interface IExchangeRateRepository : IRepository<ExchangeRate>
{
    Task<IReadOnlyDictionary<Currency, decimal>> GetRatesToTryAsync(CancellationToken cancellationToken = default);
}

/// <summary>Ledger balance of one kind of note in one cassette, summed from the movements.</summary>
public sealed record LedgerBalance(Guid AtmId, Guid CassetteId, Currency Currency, int Denomination, int Count, int Reserved);

/// <summary>Read access to the append-only ledger. Movements are written only by the unit of work.</summary>
public interface IMovementRepository
{
    Task<IReadOnlyList<CassetteMovement>> ListByAtmAsync(Guid atmId, int? slot, MovementType? type, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LedgerBalance>> GetLedgerBalancesAsync(Guid? atmId, CancellationToken cancellationToken = default);
}

/// <summary>The stored outcome of an idempotent command.</summary>
public sealed class IdempotencyRecord
{
    private IdempotencyRecord()
    {
    }

    public IdempotencyRecord(string key, string requestFingerprint, string responseJson, DateTimeOffset createdAt)
    {
        Key = key;
        RequestFingerprint = requestFingerprint;
        ResponseJson = responseJson;
        CreatedAt = createdAt;
    }

    public string Key { get; private set; } = default!;
    public string RequestFingerprint { get; private set; } = default!;
    public string ResponseJson { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
}

public interface IIdempotencyStore
{
    /// <summary>Serialises requests with the same key until the current transaction ends.</summary>
    Task LockAsync(string key, CancellationToken cancellationToken = default);

    Task<IdempotencyRecord?> FindAsync(string key, CancellationToken cancellationToken = default);

    Task AddAsync(IdempotencyRecord record, CancellationToken cancellationToken = default);
}
