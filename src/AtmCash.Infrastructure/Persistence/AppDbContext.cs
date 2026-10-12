using AtmCash.Application.Abstractions.Persistence;
using AtmCash.Domain.Atms;
using AtmCash.Domain.Common;
using AtmCash.Domain.ExchangeRates;
using AtmCash.Domain.Ledger;
using AtmCash.Domain.Reconciliations;
using AtmCash.Domain.Withdrawals;
using AtmCash.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AtmCash.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext, IUnitOfWork
{
    private IDbContextTransaction? _currentTransaction;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Atm> Atms => Set<Atm>();
    public DbSet<Cassette> Cassettes => Set<Cassette>();
    public DbSet<CassetteMovement> Movements => Set<CassetteMovement>();
    public DbSet<AtmStatusChange> AtmStatusChanges => Set<AtmStatusChange>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<Reconciliation> Reconciliations => Set<Reconciliation>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<ExchangeRateChange> ExchangeRateChanges => Set<ExchangeRateChange>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public bool HasActiveTransaction => _currentTransaction is not null;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is not null)
        {
            throw new InvalidOperationException("A transaction is already in progress.");
        }

        _currentTransaction = await Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null)
        {
            throw new InvalidOperationException("No transaction in progress.");
        }

        try
        {
            await _currentTransaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await _currentTransaction.DisposeAsync();
            _currentTransaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null)
        {
            return;
        }

        try
        {
            await _currentTransaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            await _currentTransaction.DisposeAsync();
            _currentTransaction = null;
            ChangeTracker.Clear();
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();

        // Ledger and audit: movements and status changes recorded by ATM aggregates are inserted together with the new state.
        foreach (var atm in ChangeTracker.Entries<Atm>().Select(e => e.Entity).ToList())
        {
            Movements.AddRange(atm.DequeueMovements());
            AtmStatusChanges.AddRange(atm.DequeueStatusChanges());
        }

        // Transactional outbox: domain events become outbox rows in the same transaction.
        foreach (var aggregate in ChangeTracker.Entries<AggregateRoot>().Select(e => e.Entity).ToList())
        {
            OutboxMessages.AddRange(aggregate.DequeueDomainEvents().Select(OutboxMessage.From));
        }

        // The ledger is append-only (also enforced by a database trigger).
        if (ChangeTracker.Entries<CassetteMovement>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Cassette movements are immutable and cannot be modified or deleted.");
        }

        // Audit history is append-only as well.
        if (ChangeTracker.Entries<AtmStatusChange>().Any(e => e.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<ExchangeRateChange>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Audit history records are immutable and cannot be modified or deleted.");
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
