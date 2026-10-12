using AtmCash.Domain.Atms;
using AtmCash.Domain.Common;
using AtmCash.Domain.Ledger;

namespace AtmCash.UnitTests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    public static readonly IReadOnlyDictionary<Currency, decimal> Rates = new Dictionary<Currency, decimal>
    {
        [Currency.TRY] = 1m,
        [Currency.USD] = 40m,
        [Currency.EUR] = 45m
    };

    /// <summary>TRY 100, TRY 50, TRY 20, USD 100, deposit.</summary>
    public static IReadOnlyList<CassetteConfiguration> StandardConfiguration() => new[]
    {
        new CassetteConfiguration(1, CassetteType.Recycling, Currency.TRY, 100),
        new CassetteConfiguration(2, CassetteType.Recycling, Currency.TRY, 50),
        new CassetteConfiguration(3, CassetteType.Recycling, Currency.TRY, 20),
        new CassetteConfiguration(4, CassetteType.Recycling, Currency.USD, 100),
        new CassetteConfiguration(5, CassetteType.Deposit, null, null)
    };

    public static Atm ConfiguredAtm(IReadOnlyList<CassetteConfiguration>? configuration = null)
    {
        var atm = Atm.Create("ATM-T1", "Test", null, Now);
        atm.ConfigureCassettes(configuration ?? StandardConfiguration(), "ops", Now);
        return atm;
    }

    public static Atm InServiceAtm(params (int Slot, int Count)[] load)
    {
        var atm = ConfiguredAtm();
        atm.LoadCash(load.Select(l => new CashLoadItem(l.Slot, l.Count)).ToList(), "ops", Now);
        atm.PutInService("ops", Now);
        return atm;
    }

    /// <summary>Rebuilds cassette counts from the ledger (what the consistency check does in SQL).</summary>
    public static Dictionary<int, (int Count, int Reserved)> Replay(IEnumerable<CassetteMovement> movements) =>
        movements.GroupBy(m => m.Slot)
            .ToDictionary(g => g.Key, g => (g.Sum(m => m.CountDelta), g.Sum(m => m.ReservedDelta)));
}
