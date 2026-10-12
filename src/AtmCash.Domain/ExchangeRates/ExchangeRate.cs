using AtmCash.Domain.Common;

namespace AtmCash.Domain.ExchangeRates;

/// <summary>Value of one unit of a currency in TRY. Used to value reconciliation differences and report totals.</summary>
public sealed class ExchangeRate
{
    private ExchangeRate()
    {
    }

    public ExchangeRate(Currency currency, decimal rateToTry, DateTimeOffset now)
    {
        Currency = currency;
        EnsureValid(rateToTry);
        RateToTry = rateToTry;
        UpdatedAt = now;
    }

    public Currency Currency { get; private set; }
    public decimal RateToTry { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Changes the rate and returns the audit record of the change, which the caller persists.</summary>
    public ExchangeRateChange Update(decimal rateToTry, string changedBy, DateTimeOffset now)
    {
        EnsureValid(rateToTry);

        var change = ExchangeRateChange.Create(Currency, RateToTry, rateToTry, changedBy, now);
        RateToTry = rateToTry;
        UpdatedAt = now;
        return change;
    }

    private void EnsureValid(decimal rateToTry)
    {
        if (rateToTry <= 0)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Exchange rate must be positive.");
        }

        if (Currency == Currency.TRY && rateToTry != 1m)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "The TRY rate is always 1.");
        }
    }
}
