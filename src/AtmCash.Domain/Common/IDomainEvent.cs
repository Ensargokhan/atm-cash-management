namespace AtmCash.Domain.Common;

/// <summary>
/// Something that happened in the domain. Domain events are persisted to the outbox
/// in the same transaction as the aggregate change that raised them.
/// </summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
