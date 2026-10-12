using AtmCash.Application.Features.Atms;
using AtmCash.Application.Features.Deposits;
using AtmCash.Application.Features.Reconciliations;
using AtmCash.Domain.Common;
using AtmCash.Domain.Withdrawals;

namespace AtmCash.Api.Contracts;

/// <example>{ "code": "ATM-IST-001", "name": "Kadikoy Branch", "location": "Istanbul" }</example>
public sealed record CreateAtmRequest(string Code, string? Name, string? Location);

/// <summary>Exactly five cassettes: one Deposit (no currency/denomination) and four Recycling, of which one or two are USD/EUR.</summary>
public sealed record ConfigureCassettesRequest(List<CassetteConfigurationInput> Cassettes);

public sealed record LoadCashRequest(List<CashLoadInput> Items);

public sealed record UnloadCassettesRequest(List<int> Slots);

public sealed record CreateQuoteRequest(string CardNumber, Currency Currency, int Amount);

/// <summary>Option to pay: Exact, Lower or Upper. Can be omitted when the quote is an exact match.</summary>
public sealed record ConfirmQuoteRequest(QuoteOptionKind? Option);

/// <param name="Notes">Recognised notes.</param>
/// <param name="UnrecognizedCount">Notes the machine could not recognise (returned to the customer).</param>
public sealed record DepositRequest(List<DepositNoteInput> Notes, int UnrecognizedCount);

/// <summary>Counted notes per cassette. Recycling cassettes: one entry each (currency/denomination optional). Deposit cassette: one entry per currency and denomination.</summary>
public sealed record SubmitReconciliationRequest(List<CountedNotesInput> Counts);

public sealed record UpdateExchangeRateRequest(decimal RateToTry);
