namespace AtmCash.Application.Behaviors;

/// <summary>
/// Per-request flag set by <see cref="IdempotencyBehavior{TRequest,TResponse}"/> when it returns a stored result
/// instead of executing the command. The API layer turns it into an <c>Idempotent-Replayed</c> response header.
/// </summary>
public sealed class IdempotencyContext
{
    public bool IsReplayed { get; private set; }

    public void MarkReplayed() => IsReplayed = true;
}
