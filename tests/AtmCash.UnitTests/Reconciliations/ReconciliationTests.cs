using AtmCash.Domain.Atms;
using AtmCash.Domain.Common;
using AtmCash.Domain.Ledger;
using AtmCash.Domain.Reconciliations;

namespace AtmCash.UnitTests.Reconciliations;

public class ReconciliationTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    private static Atm AtmInMaintenance()
    {
        var atm = TestData.InServiceAtm((1, 50), (2, 40), (3, 100), (4, 20));
        atm.Deposit(Guid.NewGuid(), new[] { new CurrencyNotes(Currency.EUR, 50, 2) }, Now);
        atm.EnterMaintenance("ops", Now);
        atm.DequeueMovements();
        return atm;
    }

    private static List<CountedNotes> Counts(int slot1 = 50, int slot2 = 40, int slot3 = 100, int slot4 = 20, int eur50 = 2) => new()
    {
        new(1, null, null, slot1),
        new(2, null, null, slot2),
        new(3, null, null, slot3),
        new(4, null, null, slot4),
        new(5, Currency.EUR, 50, eur50)
    };

    [Fact]
    public void Small_difference_is_applied_immediately_as_an_adjustment_movement()
    {
        var atm = AtmInMaintenance();

        var reconciliation = Reconciliation.Submit(atm, Counts(slot2: 38), TestData.Rates, "alice", Now);

        reconciliation.Status.Should().Be(ReconciliationStatus.Applied);
        reconciliation.TotalDifferenceTry.Should().Be(100m);
        atm.GetCassette(2).Count.Should().Be(38);
        atm.DequeueMovements().Should().ContainSingle()
            .Which.Should().Match<CassetteMovement>(m => m.Type == MovementType.ReconciliationAdjustment && m.CountDelta == -2);
    }

    [Fact]
    public void Difference_above_1000_TRY_waits_for_approval_and_blocks_service()
    {
        var atm = AtmInMaintenance();

        var reconciliation = Reconciliation.Submit(atm, Counts(slot1: 39), TestData.Rates, "alice", Now);

        reconciliation.Status.Should().Be(ReconciliationStatus.AwaitingApproval);
        atm.GetCassette(1).Count.Should().Be(50, "nothing is adjusted before approval");
        var act = () => atm.PutInService("ops", Now);
        act.Should().Throw<InvalidStateException>().Which.ErrorCode.Should().Be(ErrorCodes.ReconciliationAwaitingApproval);
    }

    [Fact]
    public void Foreign_currency_differences_are_valued_in_TRY()
    {
        var atm = AtmInMaintenance();

        // 1 x USD 100 at 40 = 4,000 TRY.
        var reconciliation = Reconciliation.Submit(atm, Counts(slot4: 19), TestData.Rates, "alice", Now);

        reconciliation.TotalDifferenceTry.Should().Be(4_000m);
        reconciliation.Status.Should().Be(ReconciliationStatus.AwaitingApproval);
    }

    [Fact]
    public void Surplus_and_shortage_do_not_cancel_out()
    {
        var atm = AtmInMaintenance();

        var reconciliation = Reconciliation.Submit(atm, Counts(slot1: 44, slot2: 52), TestData.Rates, "alice", Now);

        reconciliation.TotalDifferenceTry.Should().Be(1_200m);
        reconciliation.Status.Should().Be(ReconciliationStatus.AwaitingApproval);
    }

    [Fact]
    public void Submitter_cannot_approve_their_own_reconciliation()
    {
        var atm = AtmInMaintenance();
        var reconciliation = Reconciliation.Submit(atm, Counts(slot1: 30), TestData.Rates, "alice", Now);

        var act = () => reconciliation.Approve(atm, "ALICE", Now);

        act.Should().Throw<DomainException>().Which.ErrorCode.Should().Be(ErrorCodes.FourEyesViolation);
    }

    [Fact]
    public void Approval_by_another_user_applies_adjustments_and_unblocks_service()
    {
        var atm = AtmInMaintenance();
        var reconciliation = Reconciliation.Submit(atm, Counts(slot1: 30), TestData.Rates, "alice", Now);

        reconciliation.Approve(atm, "bob", Now);
        atm.PutInService("ops", Now);

        reconciliation.Status.Should().Be(ReconciliationStatus.Approved);
        atm.GetCassette(1).Count.Should().Be(30);
        atm.Status.Should().Be(AtmStatus.InService);
    }

    [Fact]
    public void Deposit_cassette_is_counted_per_currency_and_denomination()
    {
        var atm = AtmInMaintenance();
        var counts = Counts(eur50: 1);
        counts.Add(new CountedNotes(5, Currency.TRY, 20, 3));

        var reconciliation = Reconciliation.Submit(atm, counts, TestData.Rates, "alice", Now);

        reconciliation.Lines.Where(l => l.Slot == 5).Select(l => (l.Currency, l.Denomination, l.Difference))
            .Should().BeEquivalentTo(new[] { (Currency.EUR, 50, -1), (Currency.TRY, 20, 3) });
        reconciliation.TotalDifferenceTry.Should().Be(1 * 50 * 45m + 3 * 20m);

        reconciliation.Approve(atm, "bob", Now);
        atm.GetCassette(5).CountOf(Currency.TRY, 20).Should().Be(3);
        atm.GetCassette(5).CountOf(Currency.EUR, 50).Should().Be(1);
    }

    [Fact]
    public void Reconciliation_is_only_allowed_in_maintenance()
    {
        var atm = TestData.InServiceAtm((1, 10));

        var act = () => Reconciliation.Submit(atm, Counts(), TestData.Rates, "alice", Now);

        act.Should().Throw<InvalidStateException>().Which.ErrorCode.Should().Be(ErrorCodes.OperationNotAllowedInState);
    }
}
