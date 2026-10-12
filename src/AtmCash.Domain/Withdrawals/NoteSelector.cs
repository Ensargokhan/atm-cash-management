using AtmCash.Domain.Common;

namespace AtmCash.Domain.Withdrawals;

/// <summary>A set of notes that pays an amount.</summary>
public sealed record NoteBreakdown(IReadOnlyList<NoteCount> Notes)
{
    public int Amount => Notes.Sum(n => n.Amount);
    public int NoteTotal => Notes.Sum(n => n.Count);

    public override string ToString() =>
        $"{Amount} = " + string.Join(" + ", Notes.Select(n => $"{n.Denomination}x{n.Count}"));
}

/// <summary>
/// Either an exact breakdown, or the closest payable amounts below and above the requested amount.
/// </summary>
public sealed record NoteSelectionResult(int RequestedAmount, NoteBreakdown? Exact, NoteBreakdown? Lower, NoteBreakdown? Upper)
{
    public bool IsExactMatch => Exact is not null;
    public bool HasAnyOption => Exact is not null || Lower is not null || Upper is not null;
}

/// <summary>
/// Finds the breakdown that pays an amount with the fewest notes, given limited notes per denomination
/// (bounded change-making, solved with dynamic programming).
///
/// Why not greedy: "largest note first" fails with limited stock, e.g. 160 from {50×3, 20×10}:
/// greedy takes 50×3 and is left with 10, while 50×2 + 20×3 pays it exactly.
///
/// Approach: all denominations are multiples of their GCD (10), so amounts are indexed in units of the GCD.
/// best[s] = fewest notes that pay exactly s units using the denominations processed so far.
/// Each denomination d with c notes is folded in with
///     best'[s] = min over k in 0..min(c, s/d) of best[s - k·d] + k
/// and the chosen k is kept per denomination so a breakdown can be rebuilt for any amount.
/// One pass therefore answers the exact amount and every candidate for the lower/upper alternatives.
///
/// Size: an ATM has 4 recycling cassettes of at most 100 notes, so at most 400 notes and
/// 200,000 per currency → ≤ 20,000 states × ≤ 400 notes ≈ 8M simple steps in the worst case.
///
/// Ties (same note count) are broken in favour of larger denominations.
/// </summary>
public static class NoteSelector
{
    private const int Unreachable = int.MaxValue;

    /// <param name="availableNotes">Denomination → number of notes that may be used.</param>
    /// <param name="requestedAmount">Amount the customer asked for.</param>
    /// <param name="maxAmount">Upper bound for the upper alternative (the customer's remaining daily limit).</param>
    public static NoteSelectionResult Select(IReadOnlyDictionary<int, int> availableNotes, int requestedAmount, int maxAmount = int.MaxValue)
    {
        if (requestedAmount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedAmount), requestedAmount, "Requested amount must be positive.");
        }

        var stock = availableNotes
            .Where(kv => kv.Key > 0 && kv.Value > 0)
            .OrderBy(kv => kv.Key)
            .Select(kv => (Denomination: kv.Key, Count: kv.Value))
            .ToArray();

        if (stock.Length == 0)
        {
            return new NoteSelectionResult(requestedAmount, null, null, null);
        }

        var unit = stock.Select(s => s.Denomination).Aggregate(Gcd);
        var totalUnits = stock.Sum(s => s.Denomination / unit * s.Count);

        var best = new int[totalUnits + 1];
        Array.Fill(best, Unreachable);
        best[0] = 0;

        // chosen[i][s] = how many notes of stock[i] the optimal breakdown of s uses (after folding in stock[0..i]).
        var chosen = new int[stock.Length][];
        var reach = 0;

        for (var i = 0; i < stock.Length; i++)
        {
            var step = stock[i].Denomination / unit;
            var count = stock[i].Count;
            reach += step * count;

            var next = new int[totalUnits + 1];
            Array.Fill(next, Unreachable);
            chosen[i] = new int[totalUnits + 1];

            for (var s = 0; s <= reach; s++)
            {
                var maxK = Math.Min(count, s / step);
                for (var k = 0; k <= maxK; k++)
                {
                    var previous = best[s - k * step];
                    if (previous == Unreachable)
                    {
                        continue;
                    }

                    // "<=" keeps the larger k on ties: more notes of the larger denomination.
                    if (previous + k <= next[s])
                    {
                        next[s] = previous + k;
                        chosen[i][s] = k;
                    }
                }
            }

            best = next;
        }

        NoteBreakdown Rebuild(int units)
        {
            var notes = new List<NoteCount>();
            for (var i = stock.Length - 1; i >= 0; i--)
            {
                var k = chosen[i][units];
                if (k > 0)
                {
                    notes.Add(new NoteCount(stock[i].Denomination, k));
                }

                units -= k * (stock[i].Denomination / unit);
            }

            return new NoteBreakdown(notes);
        }

        bool Payable(int units) => units >= 0 && units <= totalUnits && best[units] != Unreachable;

        if (requestedAmount % unit == 0 && Payable(requestedAmount / unit))
        {
            return new NoteSelectionResult(requestedAmount, Rebuild(requestedAmount / unit), null, null);
        }

        // Largest payable amount strictly below the request (0 does not count as an alternative).
        NoteBreakdown? lower = null;
        for (var s = Math.Min(totalUnits, (requestedAmount - 1) / unit); s > 0; s--)
        {
            if (Payable(s))
            {
                lower = Rebuild(s);
                break;
            }
        }

        // Smallest payable amount strictly above the request, within the limit.
        NoteBreakdown? upper = null;
        var maxUnits = (int)Math.Min(totalUnits, (long)maxAmount / unit);
        for (var s = requestedAmount / unit + 1; s <= maxUnits; s++)
        {
            if (Payable(s))
            {
                upper = Rebuild(s);
                break;
            }
        }

        return new NoteSelectionResult(requestedAmount, null, lower, upper);
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
