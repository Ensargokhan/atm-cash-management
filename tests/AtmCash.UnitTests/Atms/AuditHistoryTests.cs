using AtmCash.Domain.Atms;
using AtmCash.Domain.Common;
using AtmCash.Domain.ExchangeRates;

namespace AtmCash.UnitTests.Atms;

public class AuditHistoryTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    [Fact]
    public void Every_status_transition_is_recorded_with_the_user_who_made_it()
    {
        var atm = Atm.Create("ATM-1", "Test", null, Now);

        atm.ConfigureCassettes(TestData.StandardConfiguration(), "setup.user", Now);
        atm.LoadCash(new[] { new CashLoadItem(1, 10) }, "loader", Now);
        atm.PutInService("supervisor", Now);
        atm.EnterMaintenance("technician", Now.AddHours(1));

        var changes = atm.DequeueStatusChanges();

        changes.Select(c => (c.FromStatus, c.ToStatus, c.PerformedBy)).Should().Equal(
            (AtmStatus.Unconfigured, AtmStatus.Configured, "setup.user"),
            (AtmStatus.Configured, AtmStatus.Loaded, "loader"),
            (AtmStatus.Loaded, AtmStatus.InService, "supervisor"),
            (AtmStatus.InService, AtmStatus.Maintenance, "technician"));
        changes.Should().OnlyContain(c => c.AtmId == atm.Id);
        changes[^1].OccurredAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void Operations_that_keep_the_status_record_no_transition()
    {
        var atm = TestData.InServiceAtm((1, 10));
        atm.EnterMaintenance("ops", Now);
        atm.DequeueStatusChanges();

        atm.ConfigureCassettes(TestData.StandardConfiguration(), "ops", Now);
        atm.LoadCash(new[] { new CashLoadItem(1, 5) }, "ops", Now);

        atm.DequeueStatusChanges().Should().BeEmpty();
    }

    [Fact]
    public void A_rejected_transition_records_nothing()
    {
        var atm = TestData.InServiceAtm((1, 10));
        atm.DequeueStatusChanges();

        var act = () => atm.PutInService("ops", Now);

        act.Should().Throw<InvalidStateException>();
        atm.DequeueStatusChanges().Should().BeEmpty();
    }

    [Fact]
    public void Updating_an_exchange_rate_returns_the_old_and_new_rate_and_who_changed_it()
    {
        var rate = new ExchangeRate(Currency.USD, 49.33m, Now);

        var change = rate.Update(50.10m, "treasury", Now.AddDays(1));

        rate.RateToTry.Should().Be(50.10m);
        change.Currency.Should().Be(Currency.USD);
        change.OldRateToTry.Should().Be(49.33m);
        change.NewRateToTry.Should().Be(50.10m);
        change.ChangedBy.Should().Be("treasury");
        change.ChangedAt.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public void An_invalid_exchange_rate_is_rejected_and_the_rate_is_kept()
    {
        var rate = new ExchangeRate(Currency.EUR, 55.16m, Now);

        var act = () => rate.Update(0m, "treasury", Now);

        act.Should().Throw<DomainException>();
        rate.RateToTry.Should().Be(55.16m);
    }
}
