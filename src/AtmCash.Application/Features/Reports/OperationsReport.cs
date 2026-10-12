using AtmCash.Application.Abstractions.Messaging;
using AtmCash.Application.Abstractions.Persistence;
using AtmCash.Application.Common;
using AtmCash.Application.Features.Atms;
using AtmCash.Domain.Atms;
using AtmCash.Domain.Common;
using MediatR;

namespace AtmCash.Application.Features.Reports;

public sealed record CassetteReportDto(
    int Slot,
    CassetteType Type,
    Currency? Currency,
    int? Denomination,
    int Capacity,
    int NoteCount,
    int ReservedCount,
    int AvailableCount,
    bool IsLowCash,
    IReadOnlyList<CurrencyNotesDto> Contents);

public sealed record CurrencyTotalDto(
    Currency Currency,
    int NoteCount,
    int ReservedCount,
    int AvailableCount,
    int Amount,
    int ReservedAmount,
    int AvailableAmount,
    decimal RateToTry,
    decimal AmountTry);

public sealed record AtmReportDto(
    Guid AtmId,
    string Code,
    string Name,
    AtmStatus Status,
    IReadOnlyList<CassetteReportDto> Cassettes,
    IReadOnlyList<CurrencyTotalDto> Totals,
    decimal TotalTry);

public sealed record OperationsReportDto(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<AtmReportDto> Atms,
    IReadOnlyList<CurrencyTotalDto> NetworkTotals,
    decimal NetworkTotalTry);

/// <summary>Per ATM and cassette: note, reserved and available counts, with totals per currency and in TRY.</summary>
public sealed record GetOperationsReportQuery(Guid? AtmId) : IQuery<OperationsReportDto>;

public sealed class GetOperationsReportQueryHandler : IRequestHandler<GetOperationsReportQuery, OperationsReportDto>
{
    private readonly IAtmRepository _atms;
    private readonly IExchangeRateRepository _rates;
    private readonly TimeProvider _time;

    public GetOperationsReportQueryHandler(IAtmRepository atms, IExchangeRateRepository rates, TimeProvider time)
    {
        _atms = atms;
        _rates = rates;
        _time = time;
    }

    public async Task<OperationsReportDto> Handle(GetOperationsReportQuery request, CancellationToken cancellationToken)
    {
        var atms = await _atms.ListWithCassettesAsync(request.AtmId, cancellationToken);
        if (request.AtmId is not null && atms.Count == 0)
        {
            throw NotFoundException.Atm(request.AtmId.Value);
        }

        var rates = await _rates.GetRatesToTryAsync(cancellationToken);

        var atmReports = atms.Select(atm =>
        {
            var cassettes = atm.Cassettes.Select(c => new CassetteReportDto(
                c.Slot, c.Type, c.Currency, c.Denomination, c.Capacity,
                c.Count, c.ReservedCount, c.AvailableCount, c.IsLowCash,
                c.Contents().Select(n => new CurrencyNotesDto(n.Currency, n.Denomination, n.Count)).ToList())).ToList();

            var totals = Totals(atm.Cassettes.SelectMany(Lines), rates);
            return new AtmReportDto(atm.Id, atm.Code, atm.Name, atm.Status, cassettes, totals, totals.Sum(t => t.AmountTry));
        }).ToList();

        var networkTotals = Totals(atms.SelectMany(a => a.Cassettes).SelectMany(Lines), rates);

        return new OperationsReportDto(_time.GetUtcNow(), atmReports, networkTotals, networkTotals.Sum(t => t.AmountTry));
    }

    private sealed record Line(Currency Currency, int Denomination, int Count, int Reserved);

    /// <summary>Recycling cassettes can have reservations; the deposit cassette never has.</summary>
    private static IEnumerable<Line> Lines(Cassette cassette) =>
        cassette.IsRecycling
            ? new[] { new Line(cassette.Currency!.Value, cassette.Denomination!.Value, cassette.Count, cassette.ReservedCount) }
            : cassette.Contents().Select(n => new Line(n.Currency, n.Denomination, n.Count, 0));

    private static List<CurrencyTotalDto> Totals(IEnumerable<Line> lines, IReadOnlyDictionary<Currency, decimal> rates) =>
        lines.GroupBy(l => l.Currency)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var rate = rates.TryGetValue(g.Key, out var r)
                    ? r
                    : throw new DomainException(ErrorCodes.ExchangeRateMissing, $"No exchange rate for {g.Key}.");
                var amount = g.Sum(l => l.Count * l.Denomination);
                var reservedAmount = g.Sum(l => l.Reserved * l.Denomination);
                return new CurrencyTotalDto(
                    g.Key,
                    g.Sum(l => l.Count),
                    g.Sum(l => l.Reserved),
                    g.Sum(l => l.Count - l.Reserved),
                    amount,
                    reservedAmount,
                    amount - reservedAmount,
                    rate,
                    amount * rate);
            })
            .ToList();
}
