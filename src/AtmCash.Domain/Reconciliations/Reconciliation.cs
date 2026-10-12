using AtmCash.Domain.Atms;
using AtmCash.Domain.Common;

namespace AtmCash.Domain.Reconciliations;

public enum ReconciliationStatus
{
    /// <summary>Difference within the threshold; adjustments applied immediately.</summary>
    Applied = 1,

    /// <summary>Difference above the threshold; waiting for a second user.</summary>
    AwaitingApproval = 2,

    /// <summary>Approved by a second user; adjustments applied.</summary>
    Approved = 3,

    /// <summary>Rejected by a second user; nothing applied (the cash should be recounted).</summary>
    Rejected = 4
}

/// <summary>The physical count of one kind of note in one cassette.</summary>
public sealed record CountedNotes(int Slot, Currency? Currency, int? Denomination, int Count);

/// <summary>
/// A manual cash count compared with the system figures. Differences become
/// RECONCILIATION_ADJUSTMENT movements; above <see cref="ApprovalThresholdTry"/> a different
/// user must approve before they are applied (four-eyes principle).
/// </summary>
public sealed class Reconciliation : AggregateRoot
{
    public const decimal ApprovalThresholdTry = 1_000m;

    private readonly List<ReconciliationLine> _lines = new();

    private Reconciliation()
    {
    }

    public Guid AtmId { get; private set; }
    public ReconciliationStatus Status { get; private set; }
    public string SubmittedBy { get; private set; } = default!;
    public DateTimeOffset SubmittedAt { get; private set; }
    public string? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }

    /// <summary>Σ |difference| valued in TRY. Absolute values are used so a surplus cannot hide a shortage.</summary>
    public decimal TotalDifferenceTry { get; private set; }

    public IReadOnlyCollection<ReconciliationLine> Lines => _lines.AsReadOnly();

    public static Reconciliation Submit(
        Atm atm,
        IReadOnlyList<CountedNotes> counts,
        IReadOnlyDictionary<Currency, decimal> ratesToTry,
        string submittedBy,
        DateTimeOffset now)
    {
        atm.EnsureCanReconcile();

        var reconciliation = new Reconciliation
        {
            AtmId = atm.Id,
            SubmittedBy = submittedBy,
            SubmittedAt = now
        };

        reconciliation._lines.AddRange(BuildLines(atm, counts, ratesToTry));
        reconciliation.TotalDifferenceTry = reconciliation._lines.Sum(l => l.DifferenceValueTry);

        if (reconciliation.TotalDifferenceTry > ApprovalThresholdTry)
        {
            reconciliation.Status = ReconciliationStatus.AwaitingApproval;
            atm.MarkReconciliationPending(reconciliation.Id, now);
        }
        else
        {
            reconciliation.Status = ReconciliationStatus.Applied;
            atm.ApplyReconciliationAdjustments(reconciliation.Id, reconciliation.Differences(), submittedBy, now);
        }

        return reconciliation;
    }

    public void Approve(Atm atm, string approvedBy, DateTimeOffset now)
    {
        EnsureReviewable(approvedBy);
        atm.ApplyReconciliationAdjustments(Id, Differences(), approvedBy, now);
        Status = ReconciliationStatus.Approved;
        ReviewedBy = approvedBy;
        ReviewedAt = now;
    }

    public void Reject(Atm atm, string rejectedBy, DateTimeOffset now)
    {
        EnsureReviewable(rejectedBy);
        atm.ClearPendingReconciliation(Id, now);
        Status = ReconciliationStatus.Rejected;
        ReviewedBy = rejectedBy;
        ReviewedAt = now;
    }

    private IEnumerable<(Guid CassetteId, Currency Currency, int Denomination, int Difference)> Differences() =>
        _lines.Where(l => l.Difference != 0).Select(l => (l.CassetteId, l.Currency, l.Denomination, l.Difference));

    private void EnsureReviewable(string reviewer)
    {
        if (Status != ReconciliationStatus.AwaitingApproval)
        {
            throw new InvalidStateException(ErrorCodes.ReconciliationNotAwaitingApproval,
                $"Reconciliation {Id} is {Status}; only reconciliations awaiting approval can be reviewed.");
        }

        if (string.Equals(reviewer.Trim(), SubmittedBy.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(ErrorCodes.FourEyesViolation,
                $"Reconciliation {Id} was submitted by '{SubmittedBy}'; it must be reviewed by a different user.");
        }
    }

    private static IEnumerable<ReconciliationLine> BuildLines(
        Atm atm,
        IReadOnlyList<CountedNotes> counts,
        IReadOnlyDictionary<Currency, decimal> ratesToTry)
    {
        static DomainException Invalid(string message) => new(ErrorCodes.InvalidReconciliationCount, message);

        decimal Rate(Currency currency) =>
            ratesToTry.TryGetValue(currency, out var rate)
                ? rate
                : throw new DomainException(ErrorCodes.ExchangeRateMissing, $"No exchange rate for {currency}.");

        foreach (var count in counts)
        {
            if (count.Count < 0)
            {
                throw Invalid($"Counted notes cannot be negative (slot {count.Slot}).");
            }

            _ = atm.GetCassette(count.Slot);
        }

        var lines = new List<ReconciliationLine>();

        foreach (var cassette in atm.Cassettes)
        {
            var cassetteCounts = counts.Where(c => c.Slot == cassette.Slot).ToList();

            if (cassette.IsRecycling)
            {
                if (cassetteCounts.Count != 1)
                {
                    throw Invalid($"Recycling cassette {cassette.Slot} must be counted exactly once.");
                }

                var counted = cassetteCounts[0];
                if ((counted.Currency is not null && counted.Currency != cassette.Currency) ||
                    (counted.Denomination is not null && counted.Denomination != cassette.Denomination))
                {
                    throw Invalid($"Cassette {cassette.Slot} holds {cassette.Currency} {cassette.Denomination}; the count says {counted.Currency} {counted.Denomination}.");
                }

                if (counted.Count > cassette.Capacity)
                {
                    throw Invalid($"Cassette {cassette.Slot} cannot hold {counted.Count} notes (capacity {cassette.Capacity}).");
                }

                lines.Add(ReconciliationLine.Create(cassette, cassette.Currency!.Value, cassette.Denomination!.Value,
                    cassette.Count, counted.Count, Rate(cassette.Currency!.Value)));
                continue;
            }

            // Deposit cassette: mixed content. Kinds not listed in the count are taken as counted zero.
            if (cassetteCounts.Any(c => c.Currency is null || c.Denomination is null))
            {
                throw Invalid("Each count for the deposit cassette needs a currency and a denomination.");
            }

            if (cassetteCounts.GroupBy(c => (c.Currency, c.Denomination)).Any(g => g.Count() > 1))
            {
                throw Invalid("The deposit cassette count lists the same currency and denomination more than once.");
            }

            if (cassetteCounts.Sum(c => c.Count) > cassette.Capacity)
            {
                throw Invalid($"The deposit cassette cannot hold more than {cassette.Capacity} notes.");
            }

            foreach (var counted in cassetteCounts)
            {
                Denominations.EnsureSupported(counted.Denomination!.Value);
            }

            var kinds = cassette.Items.Select(i => (Currency: i.Currency, Denomination: i.Denomination))
                .Union(cassetteCounts.Select(c => (Currency: c.Currency!.Value, Denomination: c.Denomination!.Value)))
                .OrderBy(k => k.Currency).ThenBy(k => k.Denomination);

            foreach (var (currency, denomination) in kinds)
            {
                var counted = cassetteCounts.FirstOrDefault(c => c.Currency == currency && c.Denomination == denomination)?.Count ?? 0;
                lines.Add(ReconciliationLine.Create(cassette, currency, denomination,
                    cassette.CountOf(currency, denomination), counted, Rate(currency)));
            }
        }

        return lines;
    }
}

public sealed class ReconciliationLine
{
    private ReconciliationLine()
    {
    }

    public Guid CassetteId { get; private set; }
    public int Slot { get; private set; }
    public Currency Currency { get; private set; }
    public int Denomination { get; private set; }
    public int SystemCount { get; private set; }
    public int CountedCount { get; private set; }

    /// <summary>Counted minus system. Positive = surplus, negative = shortage.</summary>
    public int Difference { get; private set; }

    /// <summary>|Difference| × denomination × rate to TRY.</summary>
    public decimal DifferenceValueTry { get; private set; }

    internal static ReconciliationLine Create(Cassette cassette, Currency currency, int denomination, int systemCount, int countedCount, decimal rateToTry)
    {
        var difference = countedCount - systemCount;
        return new ReconciliationLine
        {
            CassetteId = cassette.Id,
            Slot = cassette.Slot,
            Currency = currency,
            Denomination = denomination,
            SystemCount = systemCount,
            CountedCount = countedCount,
            Difference = difference,
            DifferenceValueTry = Math.Abs(difference) * denomination * rateToTry
        };
    }
}
