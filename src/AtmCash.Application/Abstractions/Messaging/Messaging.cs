using MediatR;

namespace AtmCash.Application.Abstractions.Messaging;

/// <summary>Marker for requests that change state. They run inside a database transaction (see TransactionBehavior).</summary>
public interface ICommandBase
{
}

/// <summary>A state-changing request (the "C" in CQRS).</summary>
public interface ICommand<out TResponse> : IRequest<TResponse>, ICommandBase
{
}

/// <summary>A read-only request (the "Q" in CQRS). Never opens a write transaction.</summary>
public interface IQuery<out TResponse> : IRequest<TResponse>
{
}

/// <summary>
/// A command that may be retried by the client with the same Idempotency-Key.
/// A retry returns the stored result instead of executing again.
/// </summary>
public interface IIdempotentCommand
{
    /// <summary>Client-supplied key (Idempotency-Key header).</summary>
    string IdempotencyKey { get; }

    /// <summary>Namespace for the key, so the same key can be used for different operations.</summary>
    string IdempotencyScope { get; }

    /// <summary>Identifies the request payload; a retry with the same key but a different payload is rejected.</summary>
    string RequestFingerprint { get; }
}

/// <summary>
/// A command performed by a member of staff. The caller must identify themselves (see CurrentUserBehavior);
/// the user id is recorded in the ledger / audit trail.
/// </summary>
public interface IRequiresUser
{
}

