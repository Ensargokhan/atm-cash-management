using AtmCash.Domain.Common;

namespace AtmCash.Domain.Atms;

/// <summary>
/// Raised when the available count of a recycling cassette falls below 10% of its capacity.
/// </summary>
public sealed record LowCashAlertRaised(
    Guid AtmId,
    string AtmCode,
    Guid CassetteId,
    int Slot,
    Currency Currency,
    int Denomination,
    int AvailableCount,
    int Capacity,
    DateTimeOffset OccurredAt) : IDomainEvent;
