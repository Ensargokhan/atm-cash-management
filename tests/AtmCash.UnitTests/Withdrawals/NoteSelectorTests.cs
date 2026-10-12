using AtmCash.Domain.Withdrawals;

namespace AtmCash.UnitTests.Withdrawals;

public class NoteSelectorTests
{
    [Fact]
    public void Upper_alternative_is_not_offered_when_it_exceeds_the_remaining_daily_limit()
    {
        var result = NoteSelector.Select(new Dictionary<int, int> { [100] = 5 }, 150, maxAmount: 150);

        result.Lower!.Amount.Should().Be(100);
        result.Upper.Should().BeNull();
    }

    [Fact]
    public void Upper_alternative_equal_to_the_limit_is_offered()
    {
        var result = NoteSelector.Select(new Dictionary<int, int> { [100] = 5 }, 150, maxAmount: 200);

        result.Upper!.Amount.Should().Be(200);
    }

    [Fact]
    public void Amount_larger_than_all_cash_returns_everything_as_lower_alternative()
    {
        var result = NoteSelector.Select(new Dictionary<int, int> { [100] = 2, [20] = 1 }, 5_000);

        result.Lower!.Amount.Should().Be(220);
        result.Upper.Should().BeNull();
    }

    [Fact]
    public void No_notes_means_no_options()
    {
        var result = NoteSelector.Select(new Dictionary<int, int>(), 100);

        result.HasAnyOption.Should().BeFalse();
    }

    [Fact]
    public void Amount_below_smallest_note_has_only_an_upper_alternative()
    {
        var result = NoteSelector.Select(new Dictionary<int, int> { [20] = 3 }, 10);

        result.Lower.Should().BeNull();
        result.Upper!.Amount.Should().Be(20);
    }

    [Fact]
    public void Amount_that_is_not_a_multiple_of_ten_gets_closest_alternatives()
    {
        var result = NoteSelector.Select(new Dictionary<int, int> { [50] = 4, [20] = 5 }, 155);

        result.Lower!.Amount.Should().Be(150);
        result.Upper!.Amount.Should().Be(160);
    }

    [Fact]
    public void Alternatives_also_use_the_fewest_notes()
    {
        // 120 can be 6x20 or 100 + 20; the latter must be chosen.
        var result = NoteSelector.Select(new Dictionary<int, int> { [100] = 1, [20] = 10 }, 130);

        result.Lower!.NoteTotal.Should().Be(2);
    }

    [Fact]
    public void Fewest_notes_is_found_where_greedy_fails()
    {
        var result = NoteSelector.Select(new Dictionary<int, int> { [500] = 1, [100] = 3, [50] = 2, [20] = 5 }, 460);

        // 460 = 100x3 + 50x2 + 20x3 = 8 notes; 500 cannot be used.
        result.Exact!.Amount.Should().Be(460);
        result.Exact.NoteTotal.Should().Be(8);
    }

    [Fact]
    public void Full_atm_is_solved_quickly()
    {
        var notes = new Dictionary<int, int> { [500] = 100, [100] = 100, [50] = 100, [20] = 100 };

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = NoteSelector.Select(notes, 45_670);
        watch.Stop();

        result.Exact!.Amount.Should().Be(45_670);
        watch.ElapsedMilliseconds.Should().BeLessThan(2_000);
    }
}
