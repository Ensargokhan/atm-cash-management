using System.Security.Cryptography;
using System.Text;
using AtmCash.Application.Abstractions.Services;
using Microsoft.Extensions.Options;

namespace AtmCash.Infrastructure.Security;

public sealed class CardHashingOptions
{
    public const string SectionName = "CardHashing";

    /// <summary>Secret key for the HMAC. Must come from a secret store in production.</summary>
    public string Pepper { get; set; } = string.Empty;
}

/// <summary>
/// HMAC-SHA256 of the card number with a server-side secret.
/// Deterministic (the same card always maps to the same key, so the daily limit can be looked up),
/// but cannot be reversed or brute-forced from the database alone, unlike a plain or salted hash of a 16-digit PAN.
/// </summary>
public sealed class HmacCardHasher : ICardHasher
{
    private readonly byte[] _key;

    public HmacCardHasher(IOptions<CardHashingOptions> options)
    {
        if (string.IsNullOrWhiteSpace(options.Value.Pepper) || options.Value.Pepper.Length < 32)
        {
            throw new InvalidOperationException($"{CardHashingOptions.SectionName}:Pepper must be configured (at least 32 characters).");
        }

        _key = Encoding.UTF8.GetBytes(options.Value.Pepper);
    }

    public string Hash(string cardNumber)
    {
        var normalized = new string(cardNumber.Where(char.IsDigit).ToArray());
        var hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
