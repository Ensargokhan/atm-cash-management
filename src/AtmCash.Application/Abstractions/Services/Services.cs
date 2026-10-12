namespace AtmCash.Application.Abstractions.Services;

/// <summary>Turns a card number into a stable, non-reversible key. Card numbers are never stored.</summary>
public interface ICardHasher
{
    string Hash(string cardNumber);
}

/// <summary>Named exclusive lock held until the current database transaction ends.</summary>
public interface IResourceLock
{
    Task AcquireAsync(string resource, CancellationToken cancellationToken = default);
}

/// <summary>
/// The member of staff performing the current request. Comes from the X-User header today;
/// a real system would read it from an authenticated token (only the API-layer implementation changes).
/// </summary>
public interface ICurrentUser
{
    /// <summary>The user id, or null when the caller did not identify themselves.</summary>
    string? UserId { get; }
}

