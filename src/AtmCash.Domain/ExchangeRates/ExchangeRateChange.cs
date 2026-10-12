using AtmCash.Domain.Common;

namespace AtmCash.Domain.ExchangeRates;

/// <summary>
/// Immutable audit record of a single exchange rate update: who changed the rate of which currency, from what to what, and when.
/// </summary>
public sealed class ExchangeRateChange
{
    private ExchangeRateChange()
    {
    }

    public Guid Id { get; private set; }
    public Currency Currency { get; private set; }
    public decimal OldRateToTry { get; private set; }
    public decimal NewRateToTry { get; private set; }
    public string ChangedBy { get; private set; } = default!;
    public DateTimeOffset ChangedAt { get; private set; }

    internal static ExchangeRateChange Create(Currency currency, decimal oldRate, decimal newRate, string changedBy, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            Currency = currency,
            OldRateToTry = oldRate,
            NewRateToTry = newRate,
            ChangedBy = changedBy,
            ChangedAt = now
        };
}
