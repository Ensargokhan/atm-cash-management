namespace AtmCash.Domain.Common;

public static class ErrorCodes
{
    // Generic
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string NotFound = "NOT_FOUND";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string InternalError = "INTERNAL_ERROR";

    // ATM lifecycle & cassettes
    public const string AtmCodeAlreadyExists = "ATM_CODE_ALREADY_EXISTS";
    public const string InvalidStateTransition = "INVALID_STATE_TRANSITION";
    public const string OperationNotAllowedInState = "OPERATION_NOT_ALLOWED_IN_STATE";
    public const string InvalidCassetteConfiguration = "INVALID_CASSETTE_CONFIGURATION";
    public const string CassetteNotFound = "CASSETTE_NOT_FOUND";
    public const string CassetteNotEmpty = "CASSETTE_NOT_EMPTY";
    public const string CassetteCapacityExceeded = "CASSETTE_CAPACITY_EXCEEDED";
    public const string InvalidCassetteOperation = "INVALID_CASSETTE_OPERATION";
    public const string UnsupportedDenomination = "UNSUPPORTED_DENOMINATION";

    // Withdrawals
    public const string InsufficientCash = "INSUFFICIENT_CASH";
    public const string DailyLimitExceeded = "DAILY_LIMIT_EXCEEDED";
    public const string QuoteExpired = "QUOTE_EXPIRED";
    public const string QuoteNotPending = "QUOTE_NOT_PENDING";
    public const string QuoteOptionNotAvailable = "QUOTE_OPTION_NOT_AVAILABLE";
    public const string IdempotencyKeyRequired = "IDEMPOTENCY_KEY_REQUIRED";
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";

    // Reconciliation
    public const string ReconciliationAwaitingApproval = "RECONCILIATION_AWAITING_APPROVAL";
    public const string ReconciliationNotAwaitingApproval = "RECONCILIATION_NOT_AWAITING_APPROVAL";
    public const string FourEyesViolation = "FOUR_EYES_VIOLATION";
    public const string UserHeaderRequired = "USER_HEADER_REQUIRED";
    public const string InvalidUser = "INVALID_USER";
    public const string InvalidReconciliationCount = "INVALID_RECONCILIATION_COUNT";
    public const string ExchangeRateMissing = "EXCHANGE_RATE_MISSING";
}
