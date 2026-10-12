namespace AtmCash.Domain.Common;

/// <summary>Every supported currency has exactly these banknotes; coins are not supported.</summary>
public static class Denominations
{
    public static readonly IReadOnlyList<int> All = new[] { 20, 50, 100, 500 };

    public static bool IsSupported(int denomination) => All.Contains(denomination);

    public static void EnsureSupported(int denomination)
    {
        if (!IsSupported(denomination))
        {
            throw new DomainException(ErrorCodes.UnsupportedDenomination,
                $"Denomination {denomination} is not supported. Supported denominations: {string.Join(", ", All)}.");
        }
    }
}
