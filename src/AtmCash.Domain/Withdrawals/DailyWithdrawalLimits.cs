using AtmCash.Domain.Common;

namespace AtmCash.Domain.Withdrawals;

/// <summary>Daily withdrawal limit per card, tracked separately for each currency.</summary>
public static class DailyWithdrawalLimits
{
    public static int For(Currency currency) => currency switch
    {
        Currency.TRY => 20_000,
        Currency.USD => 1_000,
        Currency.EUR => 1_000,
        _ => throw new ArgumentOutOfRangeException(nameof(currency), currency, null)
    };
}
