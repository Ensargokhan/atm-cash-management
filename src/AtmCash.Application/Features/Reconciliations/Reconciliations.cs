using AtmCash.Application.Abstractions.Messaging;
using AtmCash.Application.Abstractions.Persistence;
using AtmCash.Application.Common;
using AtmCash.Domain.Common;
using AtmCash.Domain.Reconciliations;
using AutoMapper;
using FluentValidation;
using MediatR;
using AtmCash.Application.Abstractions.Services;


namespace AtmCash.Application.Features.Reconciliations;

public sealed class ReconciliationLineDto
{
    public int Slot { get; init; }
    public Currency Currency { get; init; }
    public int Denomination { get; init; }
    public int SystemCount { get; init; }
    public int CountedCount { get; init; }
    public int Difference { get; init; }
    public decimal DifferenceValueTry { get; init; }
}

public sealed class ReconciliationDto
{
    public Guid Id { get; init; }
    public Guid AtmId { get; init; }
    public ReconciliationStatus Status { get; init; }
    public string SubmittedBy { get; init; } = default!;
    public DateTimeOffset SubmittedAt { get; init; }
    public string? ReviewedBy { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
    public decimal TotalDifferenceTry { get; init; }
    public decimal ApprovalThresholdTry { get; init; } = Reconciliation.ApprovalThresholdTry;
    public List<ReconciliationLineDto> Lines { get; init; } = new();
}

// ---------------------------------------------------------------- Submit

/// <param name="Currency">Optional for recycling cassettes (they hold one kind); required for the deposit cassette.</param>
public sealed record CountedNotesInput(int Slot, Currency? Currency, int? Denomination, int Count);

public sealed record SubmitReconciliationCommand(Guid AtmId, IReadOnlyList<CountedNotesInput> Counts) : ICommand<ReconciliationDto>, IRequiresUser;

public sealed class SubmitReconciliationCommandValidator : AbstractValidator<SubmitReconciliationCommand>
{
    public SubmitReconciliationCommandValidator()
    {
        RuleFor(x => x.Counts).NotEmpty();
        RuleForEach(x => x.Counts).ChildRules(count =>
        {
            count.RuleFor(c => c.Count).GreaterThanOrEqualTo(0);
            count.RuleFor(c => c.Currency).IsInEnum();
        });
    }
}

public sealed class SubmitReconciliationCommandHandler : IRequestHandler<SubmitReconciliationCommand, ReconciliationDto>
{
    private readonly IAtmRepository _atms;
    private readonly IReconciliationRepository _reconciliations;
    private readonly IExchangeRateRepository _rates;
    private readonly IMapper _mapper;
    private readonly TimeProvider _time;
    private readonly ICurrentUser _currentUser;

    public SubmitReconciliationCommandHandler(
        IAtmRepository atms,
        IReconciliationRepository reconciliations,
        IExchangeRateRepository rates,
        IMapper mapper,
        TimeProvider time,
        ICurrentUser currentUser)
    {
        _atms = atms;
        _reconciliations = reconciliations;
        _rates = rates;
        _mapper = mapper;
        _time = time;
        _currentUser = currentUser;
    }

    public async Task<ReconciliationDto> Handle(SubmitReconciliationCommand request, CancellationToken cancellationToken)
    {
        var atm = await _atms.GetForUpdateAsync(request.AtmId, cancellationToken)
                  ?? throw NotFoundException.Atm(request.AtmId);

        var rates = await _rates.GetRatesToTryAsync(cancellationToken);
        var counts = request.Counts.Select(c => new CountedNotes(c.Slot, c.Currency, c.Denomination, c.Count)).ToList();

        var reconciliation = Reconciliation.Submit(atm, counts, rates, _currentUser.UserId!, _time.GetUtcNow());
        await _reconciliations.AddAsync(reconciliation, cancellationToken);

        return _mapper.Map<ReconciliationDto>(reconciliation);
    }
}

// ---------------------------------------------------------------- Approve / Reject

public enum ReconciliationDecision
{
    Approve = 1,
    Reject = 2
}

/// <summary>Four-eyes review of a reconciliation awaiting approval. Must be done by a user other than the submitter.</summary>
public sealed record ReviewReconciliationCommand(Guid ReconciliationId, ReconciliationDecision Decision) : ICommand<ReconciliationDto>, IRequiresUser;

public sealed class ReviewReconciliationCommandValidator : AbstractValidator<ReviewReconciliationCommand>
{
    public ReviewReconciliationCommandValidator()
    {
        RuleFor(x => x.Decision).IsInEnum();
    }
}

public sealed class ReviewReconciliationCommandHandler : IRequestHandler<ReviewReconciliationCommand, ReconciliationDto>
{
    private readonly IAtmRepository _atms;
    private readonly IReconciliationRepository _reconciliations;
    private readonly IMapper _mapper;
    private readonly TimeProvider _time;
    private readonly ICurrentUser _currentUser;

    public ReviewReconciliationCommandHandler(IAtmRepository atms, IReconciliationRepository reconciliations, IMapper mapper, TimeProvider time, ICurrentUser currentUser)
    {
        _atms = atms;
        _reconciliations = reconciliations;
        _mapper = mapper;
        _time = time;
        _currentUser = currentUser;
    }

    public async Task<ReconciliationDto> Handle(ReviewReconciliationCommand request, CancellationToken cancellationToken)
    {
        // Read only the ATM id before locking: the reconciliation itself is loaded after the lock,
        // so two concurrent reviews cannot both see it as awaiting approval.
        var atmId = await _reconciliations.GetAtmIdAsync(request.ReconciliationId, cancellationToken)
                    ?? throw NotFoundException.Reconciliation(request.ReconciliationId);

        var atm = await _atms.GetForUpdateAsync(atmId, cancellationToken)
                  ?? throw NotFoundException.Atm(atmId);
        var reconciliation = await _reconciliations.GetByIdAsync(request.ReconciliationId, cancellationToken)
                             ?? throw NotFoundException.Reconciliation(request.ReconciliationId);

        var now = _time.GetUtcNow();
        var reviewer = _currentUser.UserId!;

        if (request.Decision == ReconciliationDecision.Approve)
        {
            reconciliation.Approve(atm, reviewer, now);
        }
        else
        {
            reconciliation.Reject(atm, reviewer, now);
        }

        return _mapper.Map<ReconciliationDto>(reconciliation);
    }
}

// ---------------------------------------------------------------- Queries

public sealed record GetReconciliationQuery(Guid ReconciliationId) : IQuery<ReconciliationDto>;

public sealed class GetReconciliationQueryHandler : IRequestHandler<GetReconciliationQuery, ReconciliationDto>
{
    private readonly IReconciliationRepository _reconciliations;
    private readonly IMapper _mapper;

    public GetReconciliationQueryHandler(IReconciliationRepository reconciliations, IMapper mapper)
    {
        _reconciliations = reconciliations;
        _mapper = mapper;
    }

    public async Task<ReconciliationDto> Handle(GetReconciliationQuery request, CancellationToken cancellationToken)
    {
        var reconciliation = await _reconciliations.GetByIdAsync(request.ReconciliationId, cancellationToken)
                             ?? throw NotFoundException.Reconciliation(request.ReconciliationId);

        return _mapper.Map<ReconciliationDto>(reconciliation);
    }
}

public sealed record ListReconciliationsQuery(Guid AtmId) : IQuery<IReadOnlyList<ReconciliationDto>>;

public sealed class ListReconciliationsQueryHandler : IRequestHandler<ListReconciliationsQuery, IReadOnlyList<ReconciliationDto>>
{
    private readonly IReconciliationRepository _reconciliations;
    private readonly IMapper _mapper;

    public ListReconciliationsQueryHandler(IReconciliationRepository reconciliations, IMapper mapper)
    {
        _reconciliations = reconciliations;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<ReconciliationDto>> Handle(ListReconciliationsQuery request, CancellationToken cancellationToken) =>
        _mapper.Map<List<ReconciliationDto>>(await _reconciliations.ListByAtmAsync(request.AtmId, cancellationToken));
}
