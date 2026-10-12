using AtmCash.Domain.Common;

namespace AtmCash.Domain.Atms;

/// <summary>
/// One of the five cassettes of an ATM. Part of the <see cref="Atm"/> aggregate;
/// only the aggregate root may change it.
/// </summary>
public sealed class Cassette : Entity
{
    public const int DefaultCapacity = 100;

    private readonly List<DepositCassetteItem> _items = new();

    private Cassette()
    {
    }

    internal Cassette(Guid atmId, CassetteConfiguration configuration)
    {
        AtmId = atmId;
        Slot = configuration.Slot;
        Capacity = DefaultCapacity;
        ApplyConfiguration(configuration);
    }

    public Guid AtmId { get; private set; }
    public int Slot { get; private set; }
    public CassetteType Type { get; private set; }

    /// <summary>Only set for recycling cassettes.</summary>
    public Currency? Currency { get; private set; }

    /// <summary>Only set for recycling cassettes.</summary>
    public int? Denomination { get; private set; }

    public int Capacity { get; private set; }

    /// <summary>Physical number of notes in the cassette.</summary>
    public int Count { get; private set; }

    /// <summary>Notes held for pending quotes. Only recycling cassettes can have reservations.</summary>
    public int ReservedCount { get; private set; }

    /// <summary>True while the cassette is below the low-cash threshold (used to raise the alert only once per drop).</summary>
    public bool IsLowCash { get; private set; }

    /// <summary>Content breakdown of the deposit cassette (mixed currencies and denominations).</summary>
    public IReadOnlyCollection<DepositCassetteItem> Items => _items.AsReadOnly();

    public bool IsRecycling => Type == CassetteType.Recycling;
    public bool IsDeposit => Type == CassetteType.Deposit;
    public int AvailableCount => Count - ReservedCount;
    public int FreeSpace => Capacity - Count;
    public bool IsEmpty => Count == 0 && ReservedCount == 0;

    /// <summary>Available notes relative to capacity; used to pick between two cassettes with the same denomination.</summary>
    public decimal AvailableRatio => Capacity == 0 ? 0 : (decimal)AvailableCount / Capacity;

    /// <summary>Physical notes relative to capacity.</summary>
    public decimal FillRatio => Capacity == 0 ? 0 : (decimal)Count / Capacity;

    /// <summary>Below 10% of capacity.</summary>
    public bool IsBelowLowCashThreshold => IsRecycling && AvailableCount * 10 < Capacity;

    public bool Dispenses(Currency currency, int denomination) =>
        IsRecycling && Currency == currency && Denomination == denomination;

    public bool Dispenses(Currency currency) => IsRecycling && Currency == currency;

    /// <summary>Notes of the given kind held in this cassette.</summary>
    public int CountOf(Currency currency, int denomination)
    {
        if (IsRecycling)
        {
            return Dispenses(currency, denomination) ? Count : 0;
        }

        return _items.FirstOrDefault(i => i.Currency == currency && i.Denomination == denomination)?.Count ?? 0;
    }

    /// <summary>Physical content grouped by currency and denomination.</summary>
    public IReadOnlyList<CurrencyNotes> Contents()
    {
        if (IsRecycling)
        {
            return Count > 0 ? new[] { new CurrencyNotes(Currency!.Value, Denomination!.Value, Count) } : Array.Empty<CurrencyNotes>();
        }

        return _items.Where(i => i.Count > 0)
            .Select(i => new CurrencyNotes(i.Currency, i.Denomination, i.Count))
            .ToList();
    }

    internal bool HasSameConfiguration(CassetteConfiguration configuration) =>
        Type == configuration.Type && Currency == configuration.Currency && Denomination == configuration.Denomination;

    internal void Reconfigure(CassetteConfiguration configuration)
    {
        if (HasSameConfiguration(configuration))
        {
            return;
        }

        if (!IsEmpty)
        {
            throw new InvalidStateException(ErrorCodes.CassetteNotEmpty,
                $"Cassette {Slot} still holds {Count} note(s). Unload it before changing its configuration.");
        }

        ApplyConfiguration(configuration);
    }

    private void ApplyConfiguration(CassetteConfiguration configuration)
    {
        Type = configuration.Type;
        Currency = configuration.Type == CassetteType.Recycling ? configuration.Currency : null;
        Denomination = configuration.Type == CassetteType.Recycling ? configuration.Denomination : null;
        _items.Clear();

        // An empty cassette is already "low"; the alert is raised when cash drops below the threshold, not on setup.
        IsLowCash = IsRecycling;
    }

    internal void AddRecyclingNotes(int count)
    {
        EnsureRecycling("add notes");
        EnsurePositive(count);
        if (count > FreeSpace)
        {
            throw new DomainException(ErrorCodes.CassetteCapacityExceeded,
                $"Cassette {Slot} can take {FreeSpace} more note(s); {count} requested. Capacity is {Capacity}.");
        }

        Count += count;
    }

    internal void AddDepositNotes(Currency currency, int denomination, int count)
    {
        if (!IsDeposit)
        {
            throw new DomainException(ErrorCodes.InvalidCassetteOperation, $"Cassette {Slot} is not the deposit cassette.");
        }

        EnsurePositive(count);
        if (count > FreeSpace)
        {
            throw new DomainException(ErrorCodes.CassetteCapacityExceeded,
                $"Deposit cassette can take {FreeSpace} more note(s); {count} requested.");
        }

        var item = _items.FirstOrDefault(i => i.Currency == currency && i.Denomination == denomination);
        if (item is null)
        {
            _items.Add(new DepositCassetteItem(currency, denomination, count));
        }
        else
        {
            item.Add(count);
        }

        Count += count;
    }

    internal void Reserve(int count)
    {
        EnsureRecycling("reserve notes");
        EnsurePositive(count);
        if (count > AvailableCount)
        {
            throw new DomainException(ErrorCodes.InsufficientCash,
                $"Cassette {Slot} has only {AvailableCount} available note(s); {count} requested.");
        }

        ReservedCount += count;
    }

    internal void ReleaseReservation(int count)
    {
        EnsureRecycling("release a reservation");
        EnsurePositive(count);
        if (count > ReservedCount)
        {
            throw new InvalidOperationException($"Cassette {Slot}: cannot release {count} note(s), only {ReservedCount} reserved.");
        }

        ReservedCount -= count;
    }

    internal void DispenseReserved(int count)
    {
        EnsureRecycling("dispense notes");
        EnsurePositive(count);
        if (count > ReservedCount || count > Count)
        {
            throw new InvalidOperationException($"Cassette {Slot}: cannot dispense {count} note(s), only {ReservedCount} reserved.");
        }

        ReservedCount -= count;
        Count -= count;
    }

    /// <summary>Removes everything from the cassette and returns what was removed.</summary>
    internal IReadOnlyList<CurrencyNotes> Empty()
    {
        if (ReservedCount > 0)
        {
            throw new InvalidStateException(ErrorCodes.InvalidCassetteOperation,
                $"Cassette {Slot} has {ReservedCount} reserved note(s) and cannot be unloaded.");
        }

        var removed = Contents();
        Count = 0;
        _items.Clear();
        return removed;
    }

    /// <summary>Sets the physical count of one kind of note to the counted value.</summary>
    internal void Adjust(Currency currency, int denomination, int delta)
    {
        if (delta == 0)
        {
            return;
        }

        if (IsRecycling)
        {
            if (!Dispenses(currency, denomination))
            {
                throw new DomainException(ErrorCodes.InvalidCassetteOperation,
                    $"Cassette {Slot} holds {Currency} {Denomination}, not {currency} {denomination}.");
            }

            Count += delta;
        }
        else
        {
            var item = _items.FirstOrDefault(i => i.Currency == currency && i.Denomination == denomination);
            if (item is null)
            {
                item = new DepositCassetteItem(currency, denomination, 0);
                _items.Add(item);
            }

            item.Add(delta);
            if (item.Count == 0)
            {
                _items.Remove(item);
            }

            Count += delta;
        }

        if (Count < 0 || Count > Capacity)
        {
            throw new DomainException(ErrorCodes.InvalidReconciliationCount,
                $"Cassette {Slot} would hold {Count} note(s); it must be between 0 and {Capacity}.");
        }
    }

    /// <summary>Returns true when the cassette just crossed below the low-cash threshold.</summary>
    internal bool UpdateLowCashFlag()
    {
        if (!IsRecycling)
        {
            return false;
        }

        var isLow = IsBelowLowCashThreshold;
        var crossedBelow = isLow && !IsLowCash;
        IsLowCash = isLow;
        return crossedBelow;
    }

    private void EnsureRecycling(string operation)
    {
        if (!IsRecycling)
        {
            throw new DomainException(ErrorCodes.InvalidCassetteOperation,
                $"Cannot {operation} on cassette {Slot}: it is a deposit-only cassette.");
        }
    }

    private static void EnsurePositive(int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Note count must be positive.");
        }
    }
}

/// <summary>Number of notes of one currency and denomination held in the deposit cassette.</summary>
public sealed class DepositCassetteItem
{
    private DepositCassetteItem()
    {
    }

    internal DepositCassetteItem(Currency currency, int denomination, int count)
    {
        Currency = currency;
        Denomination = denomination;
        Count = count;
    }

    public Currency Currency { get; private set; }
    public int Denomination { get; private set; }
    public int Count { get; private set; }

    internal void Add(int delta) => Count += delta;
}
