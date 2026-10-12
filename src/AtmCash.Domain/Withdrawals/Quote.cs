using AtmCash.Domain.Atms;
using AtmCash.Domain.Common;

namespace AtmCash.Domain.Withdrawals;

public enum QuoteStatus
{
    Pending = 1,
    Confirmed = 2,
    Cancelled = 3,
    Expired = 4
}

public enum QuoteOptionKind
{
    Exact = 1,
    Lower = 2,
    Upper = 3
}

/// <summary>
/// First step of a two-step withdrawal. A quote offers either the exact amount or the lower/upper
/// alternatives, and reserves the notes it offered for <see cref="Validity"/>.
///
/// When two alternatives are offered, the reservation covers both: per denomination the larger of the
/// two counts is reserved, so whichever the customer confirms is guaranteed to be payable. On confirm the
/// chosen notes are dispensed and the rest of the reservation is released.
/// </summary>
public sealed class Quote : AggregateRoot
{
    public static readonly TimeSpan Validity = TimeSpan.FromSeconds(60);

    private readonly List<QuoteOption> _options = new();
    private readonly List<QuoteReservation> _reservations = new();

    private Quote()
    {
    }

    public Guid AtmId { get; private set; }

    /// <summary>Keyed hash of the card number; the card number itself is never stored.</summary>
    public string CardHash { get; private set; } = default!;

    public Currency Currency { get; private set; }
    public int RequestedAmount { get; private set; }
    public QuoteStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Amount counted towards the daily limit while the quote is pending (the largest option).</summary>
    public int ReservedAmount { get; private set; }

    public QuoteOptionKind? ConfirmedOption { get; private set; }

    /// <summary>Amount actually paid out; set when the quote is confirmed.</summary>
    public int? DispensedAmount { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public IReadOnlyCollection<QuoteOption> Options => _options.AsReadOnly();
    public IReadOnlyCollection<QuoteReservation> Reservations => _reservations.OrderBy(r => r.Sequence).ToList().AsReadOnly();

    public bool IsExactMatch => _options.Any(o => o.Kind == QuoteOptionKind.Exact);

    /// <summary>Builds the quote and reserves the offered notes on the ATM (one atomic domain operation).</summary>
    public static Quote Create(
        Atm atm,
        string cardHash,
        Currency currency,
        NoteSelectionResult selection,
        DateTimeOffset now)
    {
        if (!selection.HasAnyOption)
        {
            throw new DomainException(ErrorCodes.InsufficientCash,
                $"ATM {atm.Code} cannot pay {selection.RequestedAmount} {currency} or any nearby amount.");
        }

        var quote = new Quote
        {
            AtmId = atm.Id,
            CardHash = cardHash,
            Currency = currency,
            RequestedAmount = selection.RequestedAmount,
            Status = QuoteStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now.Add(Validity)
        };

        if (selection.Exact is not null)
        {
            quote._options.Add(new QuoteOption(QuoteOptionKind.Exact, selection.Exact));
        }

        if (selection.Lower is not null)
        {
            quote._options.Add(new QuoteOption(QuoteOptionKind.Lower, selection.Lower));
        }

        if (selection.Upper is not null)
        {
            quote._options.Add(new QuoteOption(QuoteOptionKind.Upper, selection.Upper));
        }

        quote.ReservedAmount = quote._options.Max(o => o.Amount);

        // Per denomination, reserve enough for every offered option.
        var notesToReserve = quote._options
            .SelectMany(o => o.Notes)
            .GroupBy(n => n.Denomination)
            .Select(g => new NoteCount(g.Key, g.Max(n => n.Count)))
            .OrderByDescending(n => n.Denomination)
            .ToList();

        var allocations = atm.AllocateNotes(currency, notesToReserve);
        var sequence = 0;
        foreach (var allocation in allocations)
        {
            quote._reservations.Add(new QuoteReservation(++sequence, allocation.CassetteId, allocation.Slot, allocation.Denomination, allocation.Count));
        }

        atm.ReserveNotes(quote.Id, allocations, now);
        return quote;
    }

    public bool IsExpiredAt(DateTimeOffset now) => Status == QuoteStatus.Pending && now >= ExpiresAt;

    public QuoteOption GetOption(QuoteOptionKind kind) =>
        _options.SingleOrDefault(o => o.Kind == kind)
        ?? throw new DomainException(ErrorCodes.QuoteOptionNotAvailable,
            $"Quote {Id} does not offer the {kind} option. Offered: {string.Join(", ", _options.Select(o => o.Kind))}.");

    /// <summary>
    /// Confirms one of the offered options: dispenses its notes from the reservation and releases the rest.
    /// </summary>
    public QuoteConfirmation Confirm(Atm atm, QuoteOptionKind kind, DateTimeOffset now)
    {
        EnsureOpen(now);
        var option = GetOption(kind);

        var needed = option.Notes.ToDictionary(n => n.Denomination, n => n.Count);
        var dispense = new List<CassetteAllocation>();
        var release = new List<CassetteAllocation>();

        foreach (var reservation in Reservations)
        {
            needed.TryGetValue(reservation.Denomination, out var stillNeeded);
            var take = Math.Min(stillNeeded, reservation.Count);
            needed[reservation.Denomination] = stillNeeded - take;

            if (take > 0)
            {
                dispense.Add(reservation.ToAllocation(take));
            }

            if (reservation.Count - take > 0)
            {
                release.Add(reservation.ToAllocation(reservation.Count - take));
            }
        }

        if (needed.Values.Any(v => v > 0))
        {
            throw new InvalidOperationException($"Quote {Id}: reservation does not cover the {kind} option.");
        }

        atm.ReleaseReservation(Id, release, now);
        atm.DispenseReserved(Id, dispense, now);

        Status = QuoteStatus.Confirmed;
        ConfirmedOption = kind;
        DispensedAmount = option.Amount;
        ClosedAt = now;
        return new QuoteConfirmation(option, dispense);
    }

    public void Cancel(Atm atm, DateTimeOffset now)
    {
        EnsureOpen(now);
        ReleaseAll(atm, now);
        Status = QuoteStatus.Cancelled;
        ClosedAt = now;
    }

    /// <summary>Releases the reservation of a quote whose validity has passed.</summary>
    public void Expire(Atm atm, DateTimeOffset now)
    {
        if (Status != QuoteStatus.Pending)
        {
            return;
        }

        ReleaseAll(atm, now);
        Status = QuoteStatus.Expired;
        ClosedAt = now;
    }

    private void ReleaseAll(Atm atm, DateTimeOffset now) =>
        atm.ReleaseReservation(Id, Reservations.Select(r => r.ToAllocation(r.Count)).ToList(), now);

    private void EnsureOpen(DateTimeOffset now)
    {
        if (Status != QuoteStatus.Pending)
        {
            throw new InvalidStateException(ErrorCodes.QuoteNotPending, $"Quote {Id} is already {Status}.");
        }

        if (IsExpiredAt(now))
        {
            throw new InvalidStateException(ErrorCodes.QuoteExpired,
                $"Quote {Id} expired at {ExpiresAt:O}. Request a new quote.");
        }
    }
}

/// <summary>The confirmed option and the cassettes its notes were taken from.</summary>
public sealed record QuoteConfirmation(QuoteOption Option, IReadOnlyList<CassetteAllocation> Dispensed);

/// <summary>One of the amounts offered by a quote, with its note breakdown.</summary>
public sealed class QuoteOption
{
    private QuoteOption()
    {
    }

    internal QuoteOption(QuoteOptionKind kind, NoteBreakdown breakdown)
    {
        Kind = kind;
        Amount = breakdown.Amount;
        Notes = breakdown.Notes.Select(n => new QuoteOptionNote(n.Denomination, n.Count)).ToList();
    }

    public QuoteOptionKind Kind { get; private set; }
    public int Amount { get; private set; }
    public List<QuoteOptionNote> Notes { get; private set; } = new();
}

public sealed class QuoteOptionNote
{
    private QuoteOptionNote()
    {
    }

    internal QuoteOptionNote(int denomination, int count)
    {
        Denomination = denomination;
        Count = count;
    }

    public int Denomination { get; private set; }
    public int Count { get; private set; }
}

/// <summary>Notes held in a specific cassette for a quote. Order (sequence) follows the dispense priority.</summary>
public sealed class QuoteReservation
{
    private QuoteReservation()
    {
    }

    internal QuoteReservation(int sequence, Guid cassetteId, int slot, int denomination, int count)
    {
        Sequence = sequence;
        CassetteId = cassetteId;
        Slot = slot;
        Denomination = denomination;
        Count = count;
    }

    public int Sequence { get; private set; }
    public Guid CassetteId { get; private set; }
    public int Slot { get; private set; }
    public int Denomination { get; private set; }
    public int Count { get; private set; }

    internal CassetteAllocation ToAllocation(int count) => new(CassetteId, Slot, Denomination, count);
}
