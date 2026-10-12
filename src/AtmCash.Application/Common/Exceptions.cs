using AtmCash.Domain.Common;

namespace AtmCash.Application.Common;

public sealed class NotFoundException : Exception
{
    public NotFoundException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }

    public static NotFoundException Atm(Guid id) => new("ATM_NOT_FOUND", $"ATM '{id}' was not found.");
    public static NotFoundException Quote(Guid id) => new("QUOTE_NOT_FOUND", $"Quote '{id}' was not found.");
    public static NotFoundException Reconciliation(Guid id) => new("RECONCILIATION_NOT_FOUND", $"Reconciliation '{id}' was not found.");
    public static NotFoundException ExchangeRate(Currency currency) => new("EXCHANGE_RATE_NOT_FOUND", $"No exchange rate for {currency}.");
}

/// <summary>The request itself is malformed (e.g. a required header is missing).</summary>
public sealed class BadRequestException : Exception
{
    public BadRequestException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
