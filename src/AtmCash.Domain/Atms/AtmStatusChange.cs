namespace AtmCash.Domain.Atms;

/// <summary>
/// Immutable audit record of a single ATM status transition: who moved the ATM from which status to which, and when.
/// </summary>
public sealed class AtmStatusChange
{
    private AtmStatusChange()
    {
    }

    public Guid Id { get; private set; }
    public Guid AtmId { get; private set; }
    public AtmStatus FromStatus { get; private set; }
    public AtmStatus ToStatus { get; private set; }
    public string PerformedBy { get; private set; } = default!;
    public DateTimeOffset OccurredAt { get; private set; }

    internal static AtmStatusChange Create(Guid atmId, AtmStatus from, AtmStatus to, string performedBy, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            AtmId = atmId,
            FromStatus = from,
            ToStatus = to,
            PerformedBy = performedBy,
            OccurredAt = now
        };
}
