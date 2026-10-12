using AtmCash.Application.Abstractions.Persistence;
using AtmCash.Application.Features.Withdrawals;
using AtmCash.Infrastructure.Outbox;
using AtmCash.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AtmCash.Infrastructure.BackgroundJobs;

public sealed class BackgroundJobOptions
{
    public const string SectionName = "BackgroundJobs";

    public bool Enabled { get; set; } = true;
    public int OutboxIntervalSeconds { get; set; } = 5;
    public int OutboxBatchSize { get; set; } = 20;
    public int QuoteExpiryIntervalSeconds { get; set; } = 5;
}

/// <summary>Base loop: run a unit of work every interval, log and survive failures.</summary>
public abstract class PeriodicBackgroundService : BackgroundService
{
    private readonly ILogger _logger;

    protected PeriodicBackgroundService(IServiceScopeFactory scopeFactory, ILogger logger)
    {
        ScopeFactory = scopeFactory;
        _logger = logger;
    }

    protected IServiceScopeFactory ScopeFactory { get; }

    protected abstract TimeSpan Interval { get; }

    protected abstract Task RunOnceAsync(CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Service} iteration failed", GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}

/// <summary>
/// Reads unsent outbox messages, logs them (the "publish" step of this exercise) and marks them as sent.
/// UPDLOCK + READPAST lets several instances share the work without sending a message twice.
/// </summary>
public sealed class OutboxProcessor : PeriodicBackgroundService
{
    private readonly BackgroundJobOptions _options;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(IServiceScopeFactory scopeFactory, IOptions<BackgroundJobOptions> options, ILogger<OutboxProcessor> logger)
        : base(scopeFactory, logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(_options.OutboxIntervalSeconds);

    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var messages = await db.OutboxMessages
            .FromSql($"SELECT TOP ({_options.OutboxBatchSize}) * FROM [OutboxMessages] WITH (UPDLOCK, READPAST, ROWLOCK) WHERE [SentAt] IS NULL ORDER BY [OccurredAt]")
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                _logger.LogWarning("ALERT {EventType} (outbox {MessageId}, occurred {OccurredAt:O}): {Payload}",
                    message.Type, message.Id, message.OccurredAt, message.Payload);
                message.MarkSent(time.GetUtcNow());
            }
            catch (Exception ex)
            {
                message.MarkFailed(ex.Message);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

/// <summary>Releases the reservations of quotes whose 60-second validity has passed.</summary>
public sealed class QuoteExpiryService : PeriodicBackgroundService
{
    private readonly BackgroundJobOptions _options;
    private readonly ILogger<QuoteExpiryService> _logger;

    public QuoteExpiryService(IServiceScopeFactory scopeFactory, IOptions<BackgroundJobOptions> options, ILogger<QuoteExpiryService> logger)
        : base(scopeFactory, logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(_options.QuoteExpiryIntervalSeconds);

    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> atmIds;
        await using (var scope = ScopeFactory.CreateAsyncScope())
        {
            var quotes = scope.ServiceProvider.GetRequiredService<IQuoteRepository>();
            var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();
            atmIds = await quotes.ListAtmIdsWithExpiredQuotesAsync(time.GetUtcNow(), cancellationToken);
        }

        foreach (var atmId in atmIds)
        {
            // One scope (DbContext + transaction) per ATM keeps lock time short.
            await using var scope = ScopeFactory.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<ISender>();
            var expired = await mediator.Send(new ExpireQuotesCommand(atmId), cancellationToken);
            if (expired > 0)
            {
                _logger.LogInformation("Expired {Count} quote(s) on ATM {AtmId}", expired, atmId);
            }
        }
    }
}
