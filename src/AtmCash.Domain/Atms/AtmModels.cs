using AtmCash.Domain.Common;

namespace AtmCash.Domain.Atms;

/// <summary>Configuration of one cassette slot. Currency/denomination are only set for recycling cassettes.</summary>
public sealed record CassetteConfiguration(int Slot, CassetteType Type, Currency? Currency, int? Denomination);

/// <summary>Notes to put into a recycling cassette by hand.</summary>
public sealed record CashLoadItem(int Slot, int Count);

/// <summary>Notes of one currency and denomination.</summary>
public sealed record CurrencyNotes(Currency Currency, int Denomination, int Count);

/// <summary>A number of notes taken from (or reserved in) a specific cassette.</summary>
public sealed record CassetteAllocation(Guid CassetteId, int Slot, int Denomination, int Count);

/// <summary>Where a group of deposited notes ended up.</summary>
public sealed record DepositPlacement(int Slot, CassetteType CassetteType, Currency Currency, int Denomination, int Count);

/// <summary>Result of placing deposited notes into cassettes.</summary>
public sealed record DepositOutcome(
    IReadOnlyList<DepositPlacement> Placements,
    IReadOnlyList<CurrencyNotes> ReturnedBecauseFull);
