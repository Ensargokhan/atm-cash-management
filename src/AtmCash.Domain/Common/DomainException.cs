namespace AtmCash.Domain.Common;

/// <summary>
/// A business rule was violated. Carries a stable, machine-readable error code.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

/// <summary>
/// The operation conflicts with the current state of a resource
/// (wrong lifecycle state, quote already closed, pending approval...).
/// </summary>
public class InvalidStateException : DomainException
{
    public InvalidStateException(string errorCode, string message) : base(errorCode, message)
    {
    }
}
