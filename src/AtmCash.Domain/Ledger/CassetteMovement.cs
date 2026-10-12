using AtmCash.Domain.Common;

namespace AtmCash.Domain.Ledger;

public enum MovementType
{
    Load = 1,
    Unload = 2,
    Withdrawal = 3,
    Deposit = 4,
    Reservation = 5,
    ReservationRelease = 6,
    ReconciliationAdjustment = 7
}

/// <summary>
/// Immutable ledger record of a single change to a cassette.
/// A cassette keeps two balances: the physical note count and the reserved count.
/// Each movement carries its effect on both, so both balances can be rebuilt from the ledger:
///   Count    = Σ CountDelta
///   Reserved = Σ ReservedDelta
/// </summary>
public sealed class CassetteMovement
{
    private CassetteMovement()
    {
    }

    public Guid Id { get; private set; }
    public Guid AtmId { get; private set; }
    public Guid CassetteId { get; private set; }
    public int Slot { get; private set; }
    public MovementType Type { get; private set; }
    public Currency Currency { get; private set; }
    public int Denomination { get; private set; }

    /// <summary>Number of notes involved (always positive).</summary>
    public int Quantity { get; private set; }

    /// <summary>Signed effect on the physical note count.</summary>
    public int CountDelta { get; private set; }

    /// <summary>Signed effect on the reserved note count.</summary>
    public int ReservedDelta { get; private set; }

    /// <summary>The quote, deposit or reconciliation that caused this movement.</summary>
    public Guid? ReferenceId { get; private set; }

    public string? PerformedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    internal static CassetteMovement Create(
        Guid atmId,
        Guid cassetteId,
        int slot,
        MovementType type,
        Currency currency,
        int denomination,
        int countDelta,
        int reservedDelta,
        Guid? referenceId,
        string? performedBy,
        DateTimeOffset now)
    {
        var quantity = Math.Max(Math.Abs(countDelta), Math.Abs(reservedDelta));
        if (quantity == 0)
        {
            throw new InvalidOperationException("A movement must change at least one balance.");
        }

        return new CassetteMovement
        {
            Id = Guid.NewGuid(),
            AtmId = atmId,
            CassetteId = cassetteId,
            Slot = slot,
            Type = type,
            Currency = currency,
            Denomination = denomination,
            Quantity = quantity,
            CountDelta = countDelta,
            ReservedDelta = reservedDelta,
            ReferenceId = referenceId,
            PerformedBy = performedBy,
            CreatedAt = now
        };
    }
}
