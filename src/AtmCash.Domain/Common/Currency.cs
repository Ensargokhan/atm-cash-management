namespace AtmCash.Domain.Common;

public enum Currency
{
    TRY = 1,
    USD = 2,
    EUR = 3
}

public static class CurrencyExtensions
{
    public static bool IsForeign(this Currency currency) => currency != Currency.TRY;
}
