using AtmCash.Domain.Common;
using AtmCash.Domain.Ledger;

namespace AtmCash.Domain.Atms;

/// <summary>
/// Aggregate root for a single ATM and its five cassettes.
/// Every change to a cassette goes through this class, which enforces the lifecycle rules
/// and records an immutable <see cref="CassetteMovement"/> for each change. The movements are
/// collected here and persisted by the unit of work in the same transaction as the balances.
/// </summary>
public sealed class Atm : AggregateRoot
{
    public const int CassetteSlots = 5;
    public const int MinForeignRecyclingCassettes = 1;
    public const int MaxForeignRecyclingCassettes = 2;

    private readonly List<Cassette> _cassettes = new();
    private readonly List<CassetteMovement> _pendingMovements = new();
    private readonly List<AtmStatusChange> _pendingStatusChanges = new();


    private Atm()
    {
    }

    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Location { get; private set; }
    public AtmStatus Status { get; private set; }

    /// <summary>Set while a reconciliation waits for four-eyes approval; the ATM cannot return to service meanwhile.</summary>
    public Guid? PendingReconciliationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<Cassette> Cassettes => _cassettes.OrderBy(c => c.Slot).ToList().AsReadOnly();

    public Cassette? DepositCassette => _cassettes.SingleOrDefault(c => c.IsDeposit);

    public static Atm Create(string code, string name, string? location, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "ATM code is required.");
        }

        return new Atm
        {
            Code = code.Trim(),
            Name = string.IsNullOrWhiteSpace(name) ? code.Trim() : name.Trim(),
            Location = location?.Trim(),
            Status = AtmStatus.Unconfigured,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    public void PutInService(string performedBy, DateTimeOffset now)
    {
        if (Status == AtmStatus.InService)
        {
            throw new InvalidStateException(ErrorCodes.InvalidStateTransition,
                "The ATM is already in service.");
        }

        if (Status is not (AtmStatus.Loaded or AtmStatus.Maintenance))
        {
            throw new InvalidStateException(ErrorCodes.InvalidStateTransition,
                $"Cannot move the ATM from {Status} to {AtmStatus.InService}. Allowed from: {AtmStatus.Loaded}, {AtmStatus.Maintenance}.");
        }

        EnsureNoPendingReconciliation("return to service");
        ChangeStatus(AtmStatus.InService, performedBy, now);

    }

    /// <summary>
    /// Pending quotes must be released by the caller before switching (reserved notes cannot be
    /// counted or unloaded), which is why this checks that no notes are reserved.
    /// </summary>
    public void EnterMaintenance(string performedBy, DateTimeOffset now)
    {
        if (Status == AtmStatus.Maintenance)
        {
            throw new InvalidStateException(ErrorCodes.InvalidStateTransition,
                "The ATM is already in maintenance.");
        }

        if (Status != AtmStatus.InService)
        {
            throw new InvalidStateException(ErrorCodes.InvalidStateTransition,
                $"Cannot move the ATM from {Status} to {AtmStatus.Maintenance}. Allowed from: {AtmStatus.InService}.");
        }

        if (_cassettes.Any(c => c.ReservedCount > 0))
        {
            throw new InvalidStateException(ErrorCodes.InvalidStateTransition,
                "Pending quotes must be released before the ATM can enter maintenance.");
        }

        ChangeStatus(AtmStatus.Maintenance, performedBy, now);
    }

    // ------------------------------------------------------------------
    // Setup & maintenance
    // ------------------------------------------------------------------

    public void ConfigureCassettes(IReadOnlyList<CassetteConfiguration> configurations, string performedBy, DateTimeOffset now)
    {
        EnsureStatus("configure cassettes", AtmStatus.Unconfigured, AtmStatus.Configured, AtmStatus.Loaded, AtmStatus.Maintenance);
        EnsureNoPendingReconciliation("configure cassettes");
        ValidateConfiguration(configurations);

        foreach (var configuration in configurations.OrderBy(c => c.Slot))
        {
            var cassette = _cassettes.SingleOrDefault(c => c.Slot == configuration.Slot);
            if (cassette is null)
            {
                _cassettes.Add(new Cassette(Id, configuration));
            }
            else
            {
                cassette.Reconfigure(configuration);
            }
        }

        if (Status == AtmStatus.Unconfigured)
        {
            ChangeStatus(AtmStatus.Configured, performedBy, now);
        }

        UpdatedAt = now;
    }

    public void LoadCash(IReadOnlyList<CashLoadItem> items, string performedBy, DateTimeOffset now)
    {
        EnsureStatus("load cash", AtmStatus.Configured, AtmStatus.Loaded, AtmStatus.Maintenance);
        EnsureNoPendingReconciliation("load cash");

        if (items.Count == 0)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "At least one cassette must be loaded.");
        }

        foreach (var item in items)
        {
            var cassette = GetCassette(item.Slot);
            if (!cassette.IsRecycling)
            {
                throw new DomainException(ErrorCodes.InvalidCassetteOperation,
                    $"Cassette {item.Slot} is the deposit cassette; cash is only loaded into recycling cassettes.");
            }

            cassette.AddRecyclingNotes(item.Count);
            Record(cassette, MovementType.Load, cassette.Currency!.Value, cassette.Denomination!.Value,
                countDelta: item.Count, reservedDelta: 0, referenceId: null, performedBy, now);
        }

        if (Status == AtmStatus.Configured)
        {
            ChangeStatus(AtmStatus.Loaded, performedBy, now);
        }

        AfterCashChange(now);
    }

    /// <summary>Empties the given cassettes (e.g. removing the deposit cassette's content or swapping a cassette).</summary>
    public IReadOnlyList<CurrencyNotes> UnloadCassettes(IReadOnlyCollection<int> slots, string? performedBy, DateTimeOffset now)
    {
        EnsureStatus("unload cassettes", AtmStatus.Maintenance);
        EnsureNoPendingReconciliation("unload cassettes");

        var unloaded = new List<CurrencyNotes>();
        foreach (var slot in slots.Distinct())
        {
            var cassette = GetCassette(slot);
            foreach (var notes in cassette.Empty())
            {
                Record(cassette, MovementType.Unload, notes.Currency, notes.Denomination,
                    countDelta: -notes.Count, reservedDelta: 0, referenceId: null, performedBy, now);
                unloaded.Add(notes);
            }
        }

        AfterCashChange(now);
        return unloaded;
    }

    // ------------------------------------------------------------------
    // Withdrawals
    // ------------------------------------------------------------------

    /// <summary>Available (physical minus reserved) notes per denomination for a currency, summed over recycling cassettes.</summary>
    public IReadOnlyDictionary<int, int> AvailableNotes(Currency currency) =>
        _cassettes
            .Where(c => c.Dispenses(currency) && c.AvailableCount > 0)
            .GroupBy(c => c.Denomination!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.AvailableCount));

    /// <summary>
    /// Maps a note breakdown onto physical cassettes. When two cassettes hold the same denomination,
    /// the one with the higher fill ratio (available / capacity) is used first.
    /// </summary>
    public IReadOnlyList<CassetteAllocation> AllocateNotes(Currency currency, IEnumerable<NoteCount> notes)
    {
        var allocations = new List<CassetteAllocation>();
        foreach (var note in notes.Where(n => n.Count > 0))
        {
            var remaining = note.Count;
            var candidates = _cassettes
                .Where(c => c.Dispenses(currency, note.Denomination) && c.AvailableCount > 0)
                .OrderByDescending(c => c.AvailableRatio)
                .ThenBy(c => c.Slot);

            foreach (var cassette in candidates)
            {
                var take = Math.Min(remaining, cassette.AvailableCount);
                allocations.Add(new CassetteAllocation(cassette.Id, cassette.Slot, note.Denomination, take));
                remaining -= take;
                if (remaining == 0)
                {
                    break;
                }
            }

            if (remaining > 0)
            {
                throw new DomainException(ErrorCodes.InsufficientCash,
                    $"Not enough {currency} {note.Denomination} notes available.");
            }
        }

        return allocations;
    }

    public void ReserveNotes(Guid quoteId, IReadOnlyList<CassetteAllocation> allocations, DateTimeOffset now)
    {
        EnsureStatus("reserve notes for a withdrawal", AtmStatus.InService);

        foreach (var allocation in allocations)
        {
            var cassette = GetCassette(allocation.CassetteId);
            cassette.Reserve(allocation.Count);
            Record(cassette, MovementType.Reservation, cassette.Currency!.Value, allocation.Denomination,
                countDelta: 0, reservedDelta: allocation.Count, quoteId, performedBy: null, now);
        }

        AfterCashChange(now);
    }

    /// <summary>Releases reserved notes. Allowed in any state so expired quotes can always be cleaned up.</summary>
    public void ReleaseReservation(Guid quoteId, IReadOnlyList<CassetteAllocation> allocations, DateTimeOffset now)
    {
        foreach (var allocation in allocations.Where(a => a.Count > 0))
        {
            var cassette = GetCassette(allocation.CassetteId);
            cassette.ReleaseReservation(allocation.Count);
            Record(cassette, MovementType.ReservationRelease, cassette.Currency!.Value, allocation.Denomination,
                countDelta: 0, reservedDelta: -allocation.Count, quoteId, performedBy: null, now);
        }

        AfterCashChange(now);
    }

    /// <summary>Takes reserved notes out of the cassettes (the confirmed withdrawal).</summary>
    public void DispenseReserved(Guid quoteId, IReadOnlyList<CassetteAllocation> allocations, DateTimeOffset now)
    {
        EnsureStatus("dispense cash", AtmStatus.InService);

        foreach (var allocation in allocations.Where(a => a.Count > 0))
        {
            var cassette = GetCassette(allocation.CassetteId);
            cassette.DispenseReserved(allocation.Count);
            Record(cassette, MovementType.Withdrawal, cassette.Currency!.Value, allocation.Denomination,
                countDelta: -allocation.Count, reservedDelta: -allocation.Count, quoteId, performedBy: null, now);
        }

        AfterCashChange(now);
    }

    // ------------------------------------------------------------------
    // Deposits
    // ------------------------------------------------------------------

    /// <summary>
    /// Notes with a matching recycling cassette go there (the emptier cassette first when there are two);
    /// overflow goes to the deposit cassette; whatever still does not fit is returned to the customer.
    /// </summary>
    public DepositOutcome Deposit(Guid depositId, IReadOnlyList<CurrencyNotes> notes, DateTimeOffset now)
    {
        EnsureStatus("accept a deposit", AtmStatus.InService);

        var depositCassette = DepositCassette
            ?? throw new InvalidOperationException("ATM has no deposit cassette.");

        var placements = new List<DepositPlacement>();
        var returned = new List<CurrencyNotes>();

        var groups = notes
            .Where(n => n.Count > 0)
            .GroupBy(n => (n.Currency, n.Denomination))
            .Select(g => new CurrencyNotes(g.Key.Currency, g.Key.Denomination, g.Sum(n => n.Count)));

        foreach (var group in groups)
        {
            Denominations.EnsureSupported(group.Denomination);
            var remaining = group.Count;

            var recyclingCassettes = _cassettes
                .Where(c => c.Dispenses(group.Currency, group.Denomination))
                .OrderBy(c => c.FillRatio)
                .ThenBy(c => c.Slot);

            foreach (var cassette in recyclingCassettes)
            {
                var take = Math.Min(remaining, cassette.FreeSpace);
                if (take <= 0)
                {
                    continue;
                }

                cassette.AddRecyclingNotes(take);
                Record(cassette, MovementType.Deposit, group.Currency, group.Denomination,
                    countDelta: take, reservedDelta: 0, depositId, performedBy: null, now);
                placements.Add(new DepositPlacement(cassette.Slot, cassette.Type, group.Currency, group.Denomination, take));
                remaining -= take;
            }

            if (remaining > 0 && depositCassette.FreeSpace > 0)
            {
                var take = Math.Min(remaining, depositCassette.FreeSpace);
                depositCassette.AddDepositNotes(group.Currency, group.Denomination, take);
                Record(depositCassette, MovementType.Deposit, group.Currency, group.Denomination,
                    countDelta: take, reservedDelta: 0, depositId, performedBy: null, now);
                placements.Add(new DepositPlacement(depositCassette.Slot, depositCassette.Type, group.Currency, group.Denomination, take));
                remaining -= take;
            }

            if (remaining > 0)
            {
                returned.Add(group with { Count = remaining });
            }
        }

        AfterCashChange(now);
        return new DepositOutcome(placements, returned);
    }

    // ------------------------------------------------------------------
    // Reconciliation
    // ------------------------------------------------------------------

    public void EnsureCanReconcile()
    {
        EnsureStatus("reconcile", AtmStatus.Maintenance, AtmStatus.Configured, AtmStatus.Loaded);
        EnsureNoPendingReconciliation("start another reconciliation");
    }

    public void MarkReconciliationPending(Guid reconciliationId, DateTimeOffset now)
    {
        EnsureCanReconcile();
        PendingReconciliationId = reconciliationId;
        UpdatedAt = now;
    }

    /// <summary>Applies counted-minus-system differences as RECONCILIATION_ADJUSTMENT movements.</summary>
    public void ApplyReconciliationAdjustments(
        Guid reconciliationId,
        IEnumerable<(Guid CassetteId, Currency Currency, int Denomination, int Difference)> differences,
        string performedBy,
        DateTimeOffset now)
    {
        if (PendingReconciliationId is not null && PendingReconciliationId != reconciliationId)
        {
            throw new InvalidStateException(ErrorCodes.ReconciliationAwaitingApproval,
                $"Reconciliation {PendingReconciliationId} is awaiting approval.");
        }

        foreach (var (cassetteId, currency, denomination, difference) in differences.Where(d => d.Difference != 0))
        {
            var cassette = GetCassette(cassetteId);
            cassette.Adjust(currency, denomination, difference);
            Record(cassette, MovementType.ReconciliationAdjustment, currency, denomination,
                countDelta: difference, reservedDelta: 0, reconciliationId, performedBy, now);
        }

        PendingReconciliationId = null;
        AfterCashChange(now);
    }

    /// <summary>Called when a pending reconciliation is rejected: nothing is adjusted and the block is lifted.</summary>
    public void ClearPendingReconciliation(Guid reconciliationId, DateTimeOffset now)
    {
        if (PendingReconciliationId == reconciliationId)
        {
            PendingReconciliationId = null;
            UpdatedAt = now;
        }
    }

    // ------------------------------------------------------------------
    // Ledger plumbing
    // ------------------------------------------------------------------

    /// <summary>Movements created since the last call; the unit of work persists them with the aggregate.</summary>
    public IReadOnlyList<CassetteMovement> DequeueMovements()
    {
        var movements = _pendingMovements.ToList();
        _pendingMovements.Clear();
        return movements;
    }

    /// <summary>Status transitions made since the last call; the unit of work persists them with the aggregate.</summary>
    public IReadOnlyList<AtmStatusChange> DequeueStatusChanges()
    {
        var changes = _pendingStatusChanges.ToList();
        _pendingStatusChanges.Clear();
        return changes;
    }

    public Cassette GetCassette(int slot) =>
        _cassettes.SingleOrDefault(c => c.Slot == slot)
        ?? throw new DomainException(ErrorCodes.CassetteNotFound, $"ATM {Code} has no cassette in slot {slot}.");

    public Cassette GetCassette(Guid cassetteId) =>
        _cassettes.SingleOrDefault(c => c.Id == cassetteId)
        ?? throw new DomainException(ErrorCodes.CassetteNotFound, $"ATM {Code} has no cassette {cassetteId}.");

    private void Record(
        Cassette cassette,
        MovementType type,
        Currency currency,
        int denomination,
        int countDelta,
        int reservedDelta,
        Guid? referenceId,
        string? performedBy,
        DateTimeOffset now)
    {
        _pendingMovements.Add(CassetteMovement.Create(
            Id, cassette.Id, cassette.Slot, type, currency, denomination,
            countDelta, reservedDelta, referenceId, performedBy, now));
    }

    /// <summary>Single place where the status changes, so every transition is recorded with who made it.</summary>
    private void ChangeStatus(AtmStatus to, string performedBy, DateTimeOffset now)
    {
        _pendingStatusChanges.Add(AtmStatusChange.Create(Id, Status, to, performedBy, now));
        Status = to;
        UpdatedAt = now;
    }

    private void AfterCashChange(DateTimeOffset now)
    {
        UpdatedAt = now;
        foreach (var cassette in _cassettes)
        {
            if (cassette.UpdateLowCashFlag())
            {
                Raise(new LowCashAlertRaised(
                    Id, Code, cassette.Id, cassette.Slot, cassette.Currency!.Value, cassette.Denomination!.Value,
                    cassette.AvailableCount, cassette.Capacity, now));
            }
        }
    }

    private void EnsureStatus(string operation, params AtmStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new InvalidStateException(ErrorCodes.OperationNotAllowedInState,
                $"Cannot {operation} while the ATM is {Status}. Allowed in: {string.Join(", ", allowed)}.");
        }
    }

    private void EnsureNoPendingReconciliation(string operation)
    {
        if (PendingReconciliationId is not null)
        {
            throw new InvalidStateException(ErrorCodes.ReconciliationAwaitingApproval,
                $"Cannot {operation}: reconciliation {PendingReconciliationId} is awaiting four-eyes approval.");
        }
    }

    private static void ValidateConfiguration(IReadOnlyList<CassetteConfiguration> configurations)
    {
        static DomainException Invalid(string message) => new(ErrorCodes.InvalidCassetteConfiguration, message);

        if (configurations.Count != CassetteSlots)
        {
            throw Invalid($"Exactly {CassetteSlots} cassettes must be configured; {configurations.Count} given.");
        }

        var slots = configurations.Select(c => c.Slot).ToList();
        if (slots.Distinct().Count() != CassetteSlots || slots.Any(s => s is < 1 or > CassetteSlots))
        {
            throw Invalid($"Cassette slots must be 1 to {CassetteSlots}, each used once.");
        }

        var deposit = configurations.Where(c => c.Type == CassetteType.Deposit).ToList();
        if (deposit.Count != 1)
        {
            throw Invalid($"Exactly one deposit cassette is required; {deposit.Count} given.");
        }

        if (deposit[0].Currency is not null || deposit[0].Denomination is not null)
        {
            throw Invalid("The deposit cassette accepts any currency and denomination; do not set currency or denomination on it.");
        }

        var recycling = configurations.Where(c => c.Type == CassetteType.Recycling).ToList();
        foreach (var cassette in recycling)
        {
            if (cassette.Currency is null || cassette.Denomination is null)
            {
                throw Invalid($"Recycling cassette {cassette.Slot} needs a currency and a denomination.");
            }

            if (!Enum.IsDefined(cassette.Currency.Value))
            {
                throw Invalid($"Recycling cassette {cassette.Slot} has an unsupported currency.");
            }

            if (!Denominations.IsSupported(cassette.Denomination.Value))
            {
                throw Invalid($"Recycling cassette {cassette.Slot} has unsupported denomination {cassette.Denomination}. " +
                              $"Supported: {string.Join(", ", Denominations.All)}.");
            }
        }

        var foreign = recycling.Count(c => c.Currency!.Value.IsForeign());
        if (foreign is < MinForeignRecyclingCassettes or > MaxForeignRecyclingCassettes)
        {
            throw Invalid($"Between {MinForeignRecyclingCassettes} and {MaxForeignRecyclingCassettes} recycling cassettes " +
                          $"must hold a foreign currency (USD or EUR); {foreign} configured.");
        }
    }
}
