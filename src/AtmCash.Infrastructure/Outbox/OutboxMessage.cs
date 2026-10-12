using System.Text.Json;
using System.Text.Json.Serialization;
using AtmCash.Domain.Common;

namespace AtmCash.Infrastructure.Outbox;

/// <summary>
/// A domain event waiting to be published. Written in the same transaction as the change that raised it
/// (transactional outbox), then picked up by <see cref="OutboxProcessor"/>.
/// </summary>
public sealed class OutboxMessage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private OutboxMessage()
    {
    }

    public Guid Id { get; private set; }
    public string Type { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    public static OutboxMessage From(IDomainEvent domainEvent) => new()
    {
        Id = Guid.NewGuid(),
        Type = domainEvent.GetType().Name,
        Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), JsonOptions),
        OccurredAt = domainEvent.OccurredAt
    };

    public void MarkSent(DateTimeOffset now)
    {
        Attempts++;
        SentAt = now;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > 2000 ? error[..2000] : error;
    }
}
