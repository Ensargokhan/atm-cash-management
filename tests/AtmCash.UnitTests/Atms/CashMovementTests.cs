using AtmCash.Domain.Atms;
using AtmCash.Domain.Common;
using AtmCash.Domain.Ledger;
using AtmCash.Domain.Withdrawals;

namespace AtmCash.UnitTests.Atms;

public class DepositTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    [Fact]
    public void Notes_with_a_recycling_cassette_go_there_others_go_to_the_deposit_cassette()
    {
        var atm = TestData.InServiceAtm((1, 10));

        var outcome = atm.Deposit(Guid.NewGuid(), new[]
        {
            new CurrencyNotes(Currency.TRY, 100, 3),
            new CurrencyNotes(Currency.EUR, 50, 2),
            new CurrencyNotes(Currency.TRY, 500, 1)
        }, Now);

        outcome.Placements.Should().ContainEquivalentOf(new DepositPlacement(1, CassetteType.Recycling, Currency.TRY, 100, 3));
        outcome.Placements.Should().ContainEquivalentOf(new DepositPlacement(5, CassetteType.Deposit, Currency.EUR, 50, 2));
        outcome.Placements.Should().ContainEquivalentOf(new DepositPlacement(5, CassetteType.Deposit, Currency.TRY, 500, 1));
        outcome.ReturnedBecauseFull.Should().BeEmpty();
        atm.GetCassette(1).Count.Should().Be(13);
        atm.GetCassette(5).Count.Should().Be(3);
    }

    [Fact]
    public void Overflow_of_a_full_recycling_cassette_goes_to_the_deposit_cassette()
    {
        var atm = TestData.InServiceAtm((1, 98));

        var outcome = atm.Deposit(Guid.NewGuid(), new[] { new CurrencyNotes(Currency.TRY, 100, 5) }, Now);

        outcome.Placements.Should().BeEquivalentTo(new[]
        {
            new DepositPlacement(1, CassetteType.Recycling, Currency.TRY, 100, 2),
            new DepositPlacement(5, CassetteType.Deposit, Currency.TRY, 100, 3)
        });
        atm.GetCassette(1).Count.Should().Be(100);
    }

    [Fact]
    public void Notes_that_fit_nowhere_are_returned()
    {
        var atm = TestData.InServiceAtm((1, 10));
        atm.Deposit(Guid.NewGuid(), new[] { new CurrencyNotes(Currency.EUR, 20, 98) }, Now);

        var outcome = atm.Deposit(Guid.NewGuid(), new[] { new CurrencyNotes(Currency.EUR, 50, 5) }, Now);

        outcome.Placements.Should().ContainSingle().Which.Count.Should().Be(2);
        outcome.ReturnedBecauseFull.Should().ContainSingle().Which.Should().Be(new CurrencyNotes(Currency.EUR, 50, 3));
        atm.GetCassette(5).Count.Should().Be(100);
    }
}

public class DispenseTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    [Fact]
    public void With_two_cassettes_of_the_same_denomination_the_fuller_one_is_used_first()
    {
        var configuration = new[]
        {
            new CassetteConfiguration(1, CassetteType.Recycling, Currency.TRY, 100),
            new CassetteConfiguration(2, CassetteType.Recycling, Currency.TRY, 100),
            new CassetteConfiguration(3, CassetteType.Recycling, Currency.TRY, 20),
            new CassetteConfiguration(4, CassetteType.Recycling, Currency.USD, 100),
            new CassetteConfiguration(5, CassetteType.Deposit, null, null)
        };
        var atm = TestData.ConfiguredAtm(configuration);
        atm.LoadCash(new[] { new CashLoadItem(1, 30), new CashLoadItem(2, 80) }, "ops", Now);
        atm.PutInService("ops", Now);

        var allocations = atm.AllocateNotes(Currency.TRY, new[] { new NoteCount(100, 85) });

        allocations.Should().Equal(
            new CassetteAllocation(atm.GetCassette(2).Id, 2, 100, 80),
            new CassetteAllocation(atm.GetCassette(1).Id, 1, 100, 5));
        atm.AvailableNotes(Currency.TRY)[100].Should().Be(110);
    }
}

public class QuoteTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    private static Quote CreateQuote(Atm atm, int amount, int limit = 20_000)
    {
        var selection = NoteSelector.Select(atm.AvailableNotes(Currency.TRY), amount, limit);
        return Quote.Create(atm, "hash", Currency.TRY, selection, Now);
    }

    [Fact]
    public void Quote_reserves_enough_notes_for_both_alternatives()
    {
        // 130 from {100x5, 20x10}: lower 100+20, upper 100+20+20 -> reserve 100x1 and 20x2.
        var atm = TestData.InServiceAtm((1, 5), (3, 10));

        var quote = CreateQuote(atm, 130);

        quote.Options.Select(o => o.Amount).Should().Equal(120, 140);
        quote.ReservedAmount.Should().Be(140);
        atm.GetCassette(1).ReservedCount.Should().Be(1);
        atm.GetCassette(3).ReservedCount.Should().Be(2);
    }

    [Fact]
    public void Reserved_notes_cannot_be_offered_in_another_quote()
    {
        var atm = TestData.InServiceAtm((1, 2));
        CreateQuote(atm, 200);

        var selection = NoteSelector.Select(atm.AvailableNotes(Currency.TRY), 100);

        selection.HasAnyOption.Should().BeFalse();
    }

    [Fact]
    public void Confirming_the_lower_alternative_dispenses_it_and_releases_the_rest()
    {
        var atm = TestData.InServiceAtm((1, 5), (3, 10));
        var quote = CreateQuote(atm, 130);

        var confirmation = quote.Confirm(atm, QuoteOptionKind.Lower, Now.AddSeconds(10));

        confirmation.Option.Amount.Should().Be(120);
        quote.Status.Should().Be(QuoteStatus.Confirmed);
        atm.GetCassette(1).Count.Should().Be(4);
        atm.GetCassette(3).Count.Should().Be(9);
        atm.Cassettes.Sum(c => c.ReservedCount).Should().Be(0);
    }

    [Fact]
    public void Expired_quote_cannot_be_confirmed()
    {
        var atm = TestData.InServiceAtm((1, 5));
        var quote = CreateQuote(atm, 100);

        var act = () => quote.Confirm(atm, QuoteOptionKind.Exact, Now + Quote.Validity);

        act.Should().Throw<InvalidStateException>().Which.ErrorCode.Should().Be(ErrorCodes.QuoteExpired);
    }

    [Fact]
    public void Cancelled_quote_cannot_be_confirmed_and_its_notes_are_released()
    {
        var atm = TestData.InServiceAtm((1, 5));
        var quote = CreateQuote(atm, 300);

        quote.Cancel(atm, Now);
        var act = () => quote.Confirm(atm, QuoteOptionKind.Exact, Now);

        act.Should().Throw<InvalidStateException>().Which.ErrorCode.Should().Be(ErrorCodes.QuoteNotPending);
        atm.GetCassette(1).ReservedCount.Should().Be(0);
    }

    [Fact]
    public void Quote_without_any_payable_option_is_rejected()
    {
        var atm = TestData.InServiceAtm((4, 5)); // only USD

        var act = () => CreateQuote(atm, 100);

        act.Should().Throw<DomainException>().Which.ErrorCode.Should().Be(ErrorCodes.InsufficientCash);
    }

    [Fact]
    public void Low_cash_alert_is_raised_once_when_available_notes_fall_below_ten_percent()
    {
        var atm = TestData.InServiceAtm((1, 12));
        atm.DequeueDomainEvents();

        CreateQuote(atm, 300); // 12 -> 9 available
        CreateQuote(atm, 100); // 9 -> 8, already low

        atm.DequeueDomainEvents().OfType<LowCashAlertRaised>()
            .Should().ContainSingle().Which.Should().Match<LowCashAlertRaised>(e => e.Slot == 1 && e.AvailableCount == 9);
    }

    [Fact]
    public void Every_change_is_in_the_ledger_and_balances_can_be_rebuilt_from_it()
    {
        var atm = TestData.InServiceAtm((1, 20), (3, 50));
        var quote1 = CreateQuote(atm, 260);
        quote1.Confirm(atm, QuoteOptionKind.Exact, Now);
        var quote2 = CreateQuote(atm, 130);
        quote2.Cancel(atm, Now);
        CreateQuote(atm, 500); // still pending
        atm.Deposit(Guid.NewGuid(), new[] { new CurrencyNotes(Currency.TRY, 20, 4) }, Now);

        var movements = atm.DequeueMovements();
        var rebuilt = TestData.Replay(movements);

        foreach (var cassette in atm.Cassettes.Where(c => rebuilt.ContainsKey(c.Slot)))
        {
            rebuilt[cassette.Slot].Should().Be((cassette.Count, cassette.ReservedCount), $"slot {cassette.Slot}");
        }

        movements.Select(m => m.Type).Should().Contain(new[]
        {
            MovementType.Load, MovementType.Reservation, MovementType.Withdrawal,
            MovementType.ReservationRelease, MovementType.Deposit
        });
    }
}
