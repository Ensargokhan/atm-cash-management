using System.Text.Json;
using AtmCash.Application.Abstractions.Messaging;
using AtmCash.Application.Abstractions.Persistence;
using AtmCash.Domain.Common;
using MediatR;

namespace AtmCash.Application.Behaviors;

/// <summary>
/// Makes <see cref="IIdempotentCommand"/>s safe to retry.
///
/// Runs inside the command's transaction (registered after <see cref="TransactionBehavior{TRequest,TResponse}"/>):
///  1. take an exclusive lock on the key, so two concurrent retries cannot both execute;
///  2. if a result is stored for the key, return it (or reject if the payload differs);
///  3. otherwise execute and store the result in the same transaction as the business change.
/// A crash between execution and commit rolls back both, so a stored result always means "done exactly once".
/// </summary>
public sealed class IdempotencyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IIdempotencyStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly IdempotencyContext _context;
    public IdempotencyBehavior(IIdempotencyStore store, TimeProvider timeProvider, IdempotencyContext context)
    {
        _store = store;
        _timeProvider = timeProvider;
        _context = context;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IIdempotentCommand command)
        {
            return await next();
        }

        var key = $"{command.IdempotencyScope}:{command.IdempotencyKey}";
        await _store.LockAsync(key, cancellationToken);

        var existing = await _store.FindAsync(key, cancellationToken);
        if (existing is not null)
        {
            if (existing.RequestFingerprint != command.RequestFingerprint)
            {
                throw new DomainException(ErrorCodes.IdempotencyKeyReused,
                    "This Idempotency-Key was already used for a different request.");
            }
            _context.MarkReplayed();
            return JsonSerializer.Deserialize<TResponse>(existing.ResponseJson, JsonOptions)!;
        }

        var response = await next();

        await _store.AddAsync(new IdempotencyRecord(
            key,
            command.RequestFingerprint,
            JsonSerializer.Serialize(response, JsonOptions),
            _timeProvider.GetUtcNow()), cancellationToken);

        return response;
    }
}
