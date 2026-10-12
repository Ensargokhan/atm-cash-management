using AtmCash.Domain.Atms;
using AtmCash.Domain.Common;

namespace AtmCash.Application.Features.Atms;

public sealed record NoteDto(int Denomination, int Count);

public sealed record CurrencyNotesDto(Currency Currency, int Denomination, int Count);

public sealed class CassetteDto
{
    public Guid Id { get; init; }
    public int Slot { get; init; }
    public CassetteType Type { get; init; }
    public Currency? Currency { get; init; }
    public int? Denomination { get; init; }
    public int Capacity { get; init; }
    public int Count { get; init; }
    public int ReservedCount { get; init; }
    public int AvailableCount { get; init; }
    public decimal FillRatio { get; init; }
    public bool IsLowCash { get; init; }

    /// <summary>Content by currency and denomination (several entries only for the deposit cassette).</summary>
    public List<CurrencyNotesDto> Contents { get; init; } = new();
}

/// <summary>Notes per denomination and total balance of one currency in an ATM (all cassettes).</summary>
public sealed class CurrencyBalanceDto
{
    public Currency Currency { get; init; }
    public List<NoteDto> Notes { get; init; } = new();
    public int NoteCount { get; init; }
    public int TotalAmount { get; init; }
}

public sealed class AtmDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = default!;
    public string Name { get; init; } = default!;
    public string? Location { get; init; }
    public AtmStatus Status { get; init; }
    public Guid? PendingReconciliationId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public List<CassetteDto> Cassettes { get; init; } = new();
    public List<CurrencyBalanceDto> Balances { get; init; } = new();
}

public sealed record UnloadResultDto(AtmDto Atm, IReadOnlyList<CurrencyNotesDto> Unloaded);

public static class AtmBalances
{
    public static List<CurrencyBalanceDto> Calculate(Atm atm) =>
        atm.Cassettes
            .SelectMany(c => c.Contents())
            .GroupBy(n => n.Currency)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var notes = g.GroupBy(n => n.Denomination)
                    .OrderByDescending(d => d.Key)
                    .Select(d => new NoteDto(d.Key, d.Sum(n => n.Count)))
                    .ToList();

                return new CurrencyBalanceDto
                {
                    Currency = g.Key,
                    Notes = notes,
                    NoteCount = notes.Sum(n => n.Count),
                    TotalAmount = notes.Sum(n => n.Denomination * n.Count)
                };
            })
            .ToList();
}
