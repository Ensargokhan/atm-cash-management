using AtmCash.Domain.Atms;
using AtmCash.Domain.Common;

namespace AtmCash.UnitTests.Atms;

public class AtmLifecycleTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    [Fact]
    public void New_atm_is_unconfigured_and_follows_the_setup_path()
    {
        var atm = Atm.Create("ATM-1", "Test", null, Now);
        atm.Status.Should().Be(AtmStatus.Unconfigured);

        atm.ConfigureCassettes(TestData.StandardConfiguration(), "ops", Now);
        atm.Status.Should().Be(AtmStatus.Configured);

        atm.LoadCash(new[] { new CashLoadItem(1, 10) }, "ops", Now);
        atm.Status.Should().Be(AtmStatus.Loaded);

        atm.PutInService("ops", Now);
        atm.Status.Should().Be(AtmStatus.InService);

        atm.EnterMaintenance("ops", Now);
        atm.Status.Should().Be(AtmStatus.Maintenance);

        atm.PutInService("ops", Now);
        atm.Status.Should().Be(AtmStatus.InService);
    }

    [Fact]
    public void Cannot_go_in_service_before_cash_is_loaded()
    {
        var atm = TestData.ConfiguredAtm();

        var act = () => atm.PutInService("ops", Now);

        act.Should().Throw<InvalidStateException>().Which.ErrorCode.Should().Be(ErrorCodes.InvalidStateTransition);
    }

    [Fact]
    public void Cannot_load_cash_while_in_service()
    {
        var atm = TestData.InServiceAtm((1, 10));

        var act = () => atm.LoadCash(new[] { new CashLoadItem(1, 5) }, "ops", Now);

        act.Should().Throw<InvalidStateException>().Which.ErrorCode.Should().Be(ErrorCodes.OperationNotAllowedInState);
    }

    [Fact]
    public void Cannot_deposit_during_maintenance()
    {
        var atm = TestData.InServiceAtm((1, 10));
        atm.EnterMaintenance("ops", Now);

        var act = () => atm.Deposit(Guid.NewGuid(), new[] { new CurrencyNotes(Currency.TRY, 100, 1) }, Now);

        act.Should().Throw<InvalidStateException>().Which.ErrorCode.Should().Be(ErrorCodes.OperationNotAllowedInState);
    }

    [Fact]
    public void Cannot_enter_maintenance_from_loaded()
    {
        var atm = TestData.ConfiguredAtm();
        atm.LoadCash(new[] { new CashLoadItem(1, 10) }, "ops", Now);

        var act = () => atm.EnterMaintenance("ops", Now);

        act.Should().Throw<InvalidStateException>().Which.ErrorCode.Should().Be(ErrorCodes.InvalidStateTransition);
    }


    [Fact]
    public void Putting_an_atm_that_is_already_in_service_in_service_says_so()
    {
        var atm = TestData.InServiceAtm((1, 10));

        var act = () => atm.PutInService("ops", Now);

        act.Should().Throw<InvalidStateException>()
            .Which.Message.Should().Be("The ATM is already in service.");
    }

    [Fact]
    public void Entering_maintenance_twice_says_the_atm_is_already_in_maintenance()
    {
        var atm = TestData.InServiceAtm((1, 10));
        atm.EnterMaintenance("ops", Now);

        var act = () => atm.EnterMaintenance("ops", Now);

        act.Should().Throw<InvalidStateException>()
            .Which.Message.Should().Be("The ATM is already in maintenance.");
    }


    [Fact]
    public void Loading_beyond_capacity_is_rejected()
    {
        var atm = TestData.ConfiguredAtm();
        atm.LoadCash(new[] { new CashLoadItem(1, 90) }, "ops", Now);

        var act = () => atm.LoadCash(new[] { new CashLoadItem(1, 11) }, "ops", Now);

        act.Should().Throw<DomainException>().Which.ErrorCode.Should().Be(ErrorCodes.CassetteCapacityExceeded);
    }

    [Fact]
    public void Cash_cannot_be_loaded_into_the_deposit_cassette()
    {
        var atm = TestData.ConfiguredAtm();

        var act = () => atm.LoadCash(new[] { new CashLoadItem(5, 10) }, "ops", Now);

        act.Should().Throw<DomainException>().Which.ErrorCode.Should().Be(ErrorCodes.InvalidCassetteOperation);
    }

    [Fact]
    public void A_loaded_cassette_cannot_be_reconfigured_until_unloaded()
    {
        var atm = TestData.InServiceAtm((1, 10));
        atm.EnterMaintenance("ops", Now);
        var configuration = TestData.StandardConfiguration().ToList();
        configuration[0] = configuration[0] with { Denomination = 500 };

        var act = () => atm.ConfigureCassettes(configuration, "ops", Now);
        act.Should().Throw<InvalidStateException>().Which.ErrorCode.Should().Be(ErrorCodes.CassetteNotEmpty);

        atm.UnloadCassettes(new[] { 1 }, "ops", Now);
        atm.ConfigureCassettes(configuration, "ops", Now);
        atm.GetCassette(1).Denomination.Should().Be(500);
    }
}

public class CassetteConfigurationRulesTests
{
    private static CassetteConfiguration Recycling(int slot, Currency currency, int denomination = 100) =>
        new(slot, CassetteType.Recycling, currency, denomination);

    private static readonly CassetteConfiguration Deposit = new(5, CassetteType.Deposit, null, null);

    private static Action Configure(params CassetteConfiguration[] configuration) =>
        () => Atm.Create("A", "A", null, TestData.Now).ConfigureCassettes(configuration, "ops", TestData.Now);

    [Fact]
    public void Requires_at_least_one_foreign_currency_cassette()
    {
        Configure(Recycling(1, Currency.TRY), Recycling(2, Currency.TRY, 50), Recycling(3, Currency.TRY, 20), Recycling(4, Currency.TRY, 500), Deposit)
            .Should().Throw<DomainException>().Which.ErrorCode.Should().Be(ErrorCodes.InvalidCassetteConfiguration);
    }

    [Fact]
    public void Allows_at_most_two_foreign_currency_cassettes()
    {
        Configure(Recycling(1, Currency.TRY), Recycling(2, Currency.USD), Recycling(3, Currency.EUR), Recycling(4, Currency.USD, 50), Deposit)
            .Should().Throw<DomainException>().Which.ErrorCode.Should().Be(ErrorCodes.InvalidCassetteConfiguration);
    }

    [Fact]
    public void Two_foreign_cassettes_are_valid()
    {
        Configure(Recycling(1, Currency.TRY), Recycling(2, Currency.TRY, 20), Recycling(3, Currency.EUR), Recycling(4, Currency.USD), Deposit)
            .Should().NotThrow();
    }

    [Fact]
    public void Requires_exactly_one_deposit_cassette()
    {
        Configure(Recycling(1, Currency.TRY), Recycling(2, Currency.TRY, 50), Recycling(3, Currency.USD), Recycling(4, Currency.TRY, 20), Recycling(5, Currency.TRY, 500))
            .Should().Throw<DomainException>().Which.ErrorCode.Should().Be(ErrorCodes.InvalidCassetteConfiguration);
    }

    [Fact]
    public void Rejects_unsupported_denominations()
    {
        Configure(Recycling(1, Currency.TRY, 200), Recycling(2, Currency.TRY, 50), Recycling(3, Currency.USD), Recycling(4, Currency.TRY, 20), Deposit)
            .Should().Throw<DomainException>().Which.ErrorCode.Should().Be(ErrorCodes.InvalidCassetteConfiguration);
    }

    [Fact]
    public void Same_denomination_may_be_assigned_to_two_cassettes()
    {
        Configure(Recycling(1, Currency.TRY), Recycling(2, Currency.TRY), Recycling(3, Currency.USD), Recycling(4, Currency.TRY, 20), Deposit)
            .Should().NotThrow();
    }
}
