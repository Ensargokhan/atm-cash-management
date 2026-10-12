using AtmCash.Application.Features.Atms;
using AtmCash.Domain.Common;
using AtmCash.Domain.Withdrawals;

namespace AtmCash.Application.Features.Withdrawals;

public sealed class QuoteOptionDto
{
    public QuoteOptionKind Kind { get; init; }
    public int Amount { get; init; }
    public List<NoteDto> Notes { get; init; } = new();
}

public sealed class QuoteReservationDto
{
    public int Slot { get; init; }
    public int Denomination { get; init; }
    public int Count { get; init; }
}

public sealed class QuoteDto
{
    public Guid QuoteId { get; init; }
    public Guid AtmId { get; init; }
    public Currency Currency { get; init; }
    public int RequestedAmount { get; init; }
    public QuoteStatus Status { get; init; }
    public bool IsExactMatch { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Exact, or Lower and/or Upper alternatives.</summary>
    public List<QuoteOptionDto> Options { get; init; } = new();

    /// <summary>Notes held for this quote per cassette (covers every offered option).</summary>
    public List<QuoteReservationDto> Reservations { get; init; } = new();

    public QuoteOptionKind? ConfirmedOption { get; init; }
    public int? DispensedAmount { get; init; }

    /// <summary>Daily limit left for this card and currency after this quote (only on creation).</summary>
    public int? RemainingDailyLimit { get; set; }
}

public sealed record DispensedFromCassetteDto(int Slot, int Denomination, int Count);

public sealed record ConfirmQuoteResult(
    Guid QuoteId,
    Guid AtmId,
    QuoteStatus Status,
    QuoteOptionKind Option,
    Currency Currency,
    int DispensedAmount,
    IReadOnlyList<NoteDto> Notes,
    IReadOnlyList<DispensedFromCassetteDto> Cassettes,
    DateTimeOffset ConfirmedAt);
