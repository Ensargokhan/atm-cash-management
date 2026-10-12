using AtmCash.Application.Abstractions.Messaging;
using AtmCash.Application.Abstractions.Persistence;
using AtmCash.Application.Common;
using AtmCash.Domain.Common;
using AtmCash.Domain.Ledger;
using AutoMapper;
using MediatR;

namespace AtmCash.Application.Features.Ledger;

public sealed class MovementDto
{
    public Guid Id { get; init; }
    public Guid CassetteId { get; init; }
    public int Slot { get; init; }
    public MovementType Type { get; init; }
    public Currency Currency { get; init; }
    public int Denomination { get; init; }
    public int Quantity { get; init; }
    public int CountDelta { get; init; }
    public int ReservedDelta { get; init; }
    public Guid? ReferenceId { get; init; }
    public string? PerformedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

// ---------------------------------------------------------------- Movements

public sealed record GetMovementsQuery(Guid AtmId, int? Slot, MovementType? Type, int Take = 200) : IQuery<IReadOnlyList<MovementDto>>;

public sealed class GetMovementsQueryHandler : IRequestHandler<GetMovementsQuery, IReadOnlyList<MovementDto>>
{
    private readonly IAtmRepository _atms;
    private readonly IMovementRepository _movements;
    private readonly IMapper _mapper;

    public GetMovementsQueryHandler(IAtmRepository atms, IMovementRepository movements, IMapper mapper)
    {
        _atms = atms;
        _movements = movements;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<MovementDto>> Handle(GetMovementsQuery request, CancellationToken cancellationToken)
    {
        if (!await _atms.AnyAsync(a => a.Id == request.AtmId, cancellationToken))
        {
            throw NotFoundException.Atm(request.AtmId);
        }

        var take = Math.Clamp(request.Take, 1, 1_000);
        var movements = await _movements.ListByAtmAsync(request.AtmId, request.Slot, request.Type, take, cancellationToken);
        return _mapper.Map<List<MovementDto>>(movements);
    }
}

// ---------------------------------------------------------------- Consistency check

public sealed record LedgerDifferenceDto(
    Guid AtmId,
    string AtmCode,
    int Slot,
    string Balance,
    Currency? Currency,
    int? Denomination,
    int Stored,
    int FromLedger);

public sealed class LedgerConsistencyReportDto
{
    public DateTimeOffset CheckedAt { get; init; }
    public bool IsConsistent => Differences.Count == 0;
    public int AtmsChecked { get; init; }
    public int CassettesChecked { get; init; }
    public List<LedgerDifferenceDto> Differences { get; init; } = new();
}

/// <summary>
/// Rebuilds every cassette balance from its movements and compares it with the stored balance:
/// the physical count and the reserved count per cassette, and the content per currency/denomination.
/// </summary>
public sealed record CheckLedgerConsistencyQuery(Guid? AtmId) : IQuery<LedgerConsistencyReportDto>;

public sealed class CheckLedgerConsistencyQueryHandler : IRequestHandler<CheckLedgerConsistencyQuery, LedgerConsistencyReportDto>
{
    private readonly IAtmRepository _atms;
    private readonly IMovementRepository _movements;
    private readonly TimeProvider _time;

    public CheckLedgerConsistencyQueryHandler(IAtmRepository atms, IMovementRepository movements, TimeProvider time)
    {
        _atms = atms;
        _movements = movements;
        _time = time;
    }

    public async Task<LedgerConsistencyReportDto> Handle(CheckLedgerConsistencyQuery request, CancellationToken cancellationToken)
    {
        var atms = await _atms.ListWithCassettesAsync(request.AtmId, cancellationToken);
        if (request.AtmId is not null && atms.Count == 0)
        {
            throw NotFoundException.Atm(request.AtmId.Value);
        }

        var ledger = (await _movements.GetLedgerBalancesAsync(request.AtmId, cancellationToken))
            .ToLookup(b => b.CassetteId);

        var differences = new List<LedgerDifferenceDto>();
        var cassettesChecked = 0;

        foreach (var atm in atms)
        {
            foreach (var cassette in atm.Cassettes)
            {
                cassettesChecked++;
                var balances = ledger[cassette.Id].ToList();

                void Compare(string balance, Currency? currency, int? denomination, int stored, int fromLedger)
                {
                    if (stored != fromLedger)
                    {
                        differences.Add(new LedgerDifferenceDto(atm.Id, atm.Code, cassette.Slot, balance, currency, denomination, stored, fromLedger));
                    }
                }

                Compare("Count", null, null, cassette.Count, balances.Sum(b => b.Count));
                Compare("Reserved", null, null, cassette.ReservedCount, balances.Sum(b => b.Reserved));

                var kinds = cassette.Contents().Select(c => (c.Currency, c.Denomination))
                    .Union(balances.Where(b => b.Count != 0).Select(b => (b.Currency, b.Denomination)));

                foreach (var (currency, denomination) in kinds)
                {
                    Compare("Content", currency, denomination,
                        cassette.CountOf(currency, denomination),
                        balances.Where(b => b.Currency == currency && b.Denomination == denomination).Sum(b => b.Count));
                }
            }
        }

        return new LedgerConsistencyReportDto
        {
            CheckedAt = _time.GetUtcNow(),
            AtmsChecked = atms.Count,
            CassettesChecked = cassettesChecked,
            Differences = differences
        };
    }
}
