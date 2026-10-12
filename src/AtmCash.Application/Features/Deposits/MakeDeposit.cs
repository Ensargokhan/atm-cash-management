using AtmCash.Application.Abstractions.Messaging;
using AtmCash.Application.Abstractions.Persistence;
using AtmCash.Application.Common;
using AtmCash.Domain.Atms;
using AtmCash.Domain.Common;
using AutoMapper;
using FluentValidation;
using MediatR;

namespace AtmCash.Application.Features.Deposits;

public sealed record DepositNoteInput(Currency Currency, int Denomination, int Count);

/// <param name="Notes">Recognised notes, by currency and denomination.</param>
/// <param name="UnrecognizedCount">Notes the machine could not recognise; returned to the customer, not part of the transaction.</param>
public sealed record MakeDepositCommand(Guid AtmId, IReadOnlyList<DepositNoteInput> Notes, int UnrecognizedCount) : ICommand<DepositResultDto>;

public enum ReturnReason
{
    Unrecognized = 1,
    CassetteFull = 2
}

public sealed class DepositPlacementDto
{
    public int Slot { get; init; }
    public CassetteType CassetteType { get; init; }
    public Currency Currency { get; init; }
    public int Denomination { get; init; }
    public int Count { get; init; }
}

public sealed record ReturnedNotesDto(Currency? Currency, int? Denomination, int Count, ReturnReason Reason);

public sealed record CurrencyAmountDto(Currency Currency, int NoteCount, int Amount);

public sealed class DepositResultDto
{
    public Guid DepositId { get; init; }
    public Guid AtmId { get; init; }
    public DateTimeOffset ProcessedAt { get; init; }

    /// <summary>Which cassette each accepted note went to.</summary>
    public List<DepositPlacementDto> Accepted { get; init; } = new();

    /// <summary>Notes handed back to the customer: unrecognised ones and those that did not fit.</summary>
    public List<ReturnedNotesDto> Returned { get; init; } = new();

    /// <summary>Credited amount per currency.</summary>
    public List<CurrencyAmountDto> AcceptedTotals { get; init; } = new();
}

public sealed class MakeDepositCommandValidator : AbstractValidator<MakeDepositCommand>
{
    public MakeDepositCommandValidator()
    {
        RuleFor(x => x.Notes).NotNull();
        RuleFor(x => x.UnrecognizedCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x)
            .Must(x => (x.Notes?.Count ?? 0) > 0 || x.UnrecognizedCount > 0)
            .WithMessage("A deposit must contain at least one note.");

        RuleForEach(x => x.Notes).ChildRules(note =>
        {
            note.RuleFor(n => n.Currency).IsInEnum();
            note.RuleFor(n => n.Denomination)
                .Must(Denominations.IsSupported)
                .WithErrorCode(ErrorCodes.UnsupportedDenomination)
                .WithMessage($"Denomination must be one of {string.Join(", ", Denominations.All)}.");
            note.RuleFor(n => n.Count).InclusiveBetween(1, 1_000);
        });
    }
}

public sealed class MakeDepositCommandHandler : IRequestHandler<MakeDepositCommand, DepositResultDto>
{
    private readonly IAtmRepository _atms;
    private readonly IMapper _mapper;
    private readonly TimeProvider _time;

    public MakeDepositCommandHandler(IAtmRepository atms, IMapper mapper, TimeProvider time)
    {
        _atms = atms;
        _mapper = mapper;
        _time = time;
    }

    public async Task<DepositResultDto> Handle(MakeDepositCommand request, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var atm = await _atms.GetForUpdateAsync(request.AtmId, cancellationToken)
                  ?? throw NotFoundException.Atm(request.AtmId);

        var depositId = Guid.NewGuid();
        var outcome = atm.Deposit(
            depositId,
            request.Notes.Select(n => new CurrencyNotes(n.Currency, n.Denomination, n.Count)).ToList(),
            now);

        var returned = new List<ReturnedNotesDto>();
        if (request.UnrecognizedCount > 0)
        {
            returned.Add(new ReturnedNotesDto(null, null, request.UnrecognizedCount, ReturnReason.Unrecognized));
        }

        returned.AddRange(outcome.ReturnedBecauseFull.Select(r =>
            new ReturnedNotesDto(r.Currency, r.Denomination, r.Count, ReturnReason.CassetteFull)));

        return new DepositResultDto
        {
            DepositId = depositId,
            AtmId = atm.Id,
            ProcessedAt = now,
            Accepted = _mapper.Map<List<DepositPlacementDto>>(outcome.Placements),
            Returned = returned,
            AcceptedTotals = outcome.Placements
                .GroupBy(p => p.Currency)
                .OrderBy(g => g.Key)
                .Select(g => new CurrencyAmountDto(g.Key, g.Sum(p => p.Count), g.Sum(p => p.Count * p.Denomination)))
                .ToList()
        };
    }
}
