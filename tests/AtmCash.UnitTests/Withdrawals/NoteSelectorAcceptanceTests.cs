using AtmCash.Domain.Common;
using AtmCash.Domain.Withdrawals;

namespace AtmCash.UnitTests.Withdrawals;

/// <summary>The acceptance tests listed in section B3 of the assignment, one test per row.</summary>
public class NoteSelectorAcceptanceTests
{
    private const int TryDailyLimit = 20_000;
    private const int UsdDailyLimit = 1_000;

    private static Dictionary<int, int> Notes(params (int Denomination, int Count)[] notes) =>
        notes.ToDictionary(n => n.Denomination, n => n.Count);

    private static (int, int)[] Breakdown(NoteBreakdown? breakdown) =>
        breakdown!.Notes.OrderByDescending(n => n.Denomination).Select(n => (n.Denomination, n.Count)).ToArray();

    [Fact]
    public void Test1_TRY_160_is_paid_exactly_with_50x2_and_20x3()
    {
        var result = NoteSelector.Select(Notes((100, 0), (50, 3), (20, 10)), 160, TryDailyLimit);

        result.IsExactMatch.Should().BeTrue();
        Breakdown(result.Exact).Should().Equal((50, 2), (20, 3));
    }

    [Fact]
    public void Test2_TRY_80_has_no_exact_match_lower_70_upper_90()
    {
        var result = NoteSelector.Select(Notes((50, 1), (20, 2)), 80, TryDailyLimit);

        result.IsExactMatch.Should().BeFalse();
        result.Lower!.Amount.Should().Be(70);
        Breakdown(result.Lower).Should().Equal((50, 1), (20, 1));
        result.Upper!.Amount.Should().Be(90);
        Breakdown(result.Upper).Should().Equal((50, 1), (20, 2));
    }

    [Fact]
    public void Test3_TRY_130_has_no_exact_match_lower_120_upper_140()
    {
        var result = NoteSelector.Select(Notes((100, 5), (20, 10)), 130, TryDailyLimit);

        result.IsExactMatch.Should().BeFalse();
        result.Lower!.Amount.Should().Be(120);
        Breakdown(result.Lower).Should().Equal((100, 1), (20, 1));
        result.Upper!.Amount.Should().Be(140);
        Breakdown(result.Upper).Should().Equal((100, 1), (20, 2));
    }

    [Fact]
    public void Test4_USD_250_has_lower_200_and_no_upper()
    {
        var result = NoteSelector.Select(Notes((100, 2)), 250, UsdDailyLimit);

        result.IsExactMatch.Should().BeFalse();
        result.Lower!.Amount.Should().Be(200);
        Breakdown(result.Lower).Should().Equal((100, 2));
        result.Upper.Should().BeNull();
    }

    [Fact]
    public void Test5_TRY_600_is_paid_exactly_with_500x1_and_20x5()
    {
        var result = NoteSelector.Select(Notes((500, 1), (100, 0), (20, 5)), 600, TryDailyLimit);

        result.IsExactMatch.Should().BeTrue();
        Breakdown(result.Exact).Should().Equal((500, 1), (20, 5));
    }

    [Fact]
    public void Test6_TRY_200_uses_fewest_notes_100x1_and_50x2()
    {
        var result = NoteSelector.Select(Notes((100, 1), (50, 4), (20, 5)), 200, TryDailyLimit);

        result.IsExactMatch.Should().BeTrue();
        Breakdown(result.Exact).Should().Equal((100, 1), (50, 2));
        result.Exact!.NoteTotal.Should().Be(3, "4x50 and 100 + 5x20 use more notes");
    }

    [Fact]
    public void Test7_TRY_1000_is_paid_exactly_with_500x1_100x3_20x10()
    {
        var result = NoteSelector.Select(Notes((500, 1), (100, 3), (20, 20)), 1000, TryDailyLimit);

        result.IsExactMatch.Should().BeTrue();
        Breakdown(result.Exact).Should().Equal((500, 1), (100, 3), (20, 10));
    }

    [Fact]
    public void Test8_TRY_110_has_no_exact_match_lower_100_upper_120()
    {
        var result = NoteSelector.Select(Notes((100, 3), (50, 1), (20, 1)), 110, TryDailyLimit);

        result.IsExactMatch.Should().BeFalse();
        result.Lower!.Amount.Should().Be(100);
        Breakdown(result.Lower).Should().Equal((100, 1));
        result.Upper!.Amount.Should().Be(120);
        Breakdown(result.Upper).Should().Equal((100, 1), (20, 1));
    }
}
