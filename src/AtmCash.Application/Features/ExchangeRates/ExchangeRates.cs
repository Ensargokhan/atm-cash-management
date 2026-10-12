using AtmCash.Application.Abstractions.Messaging;
using AtmCash.Application.Abstractions.Persistence;
using AtmCash.Application.Abstractions.Services;
using AtmCash.Application.Common;
using AtmCash.Domain.Common;
using AtmCash.Domain.ExchangeRates;
using AutoMapper;
using FluentValidation;
using MediatR;

namespace AtmCash.Application.Features.ExchangeRates;

public sealed class ExchangeRateDto
{
    public Currency Currency { get; init; }
    public decimal RateToTry { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record ListExchangeRatesQuery : IQuery<IReadOnlyList<ExchangeRateDto>>;

public sealed class ListExchangeRatesQueryHandler : IRequestHandler<ListExchangeRatesQuery, IReadOnlyList<ExchangeRateDto>>
{
    private readonly IExchangeRateRepository _rates;
    private readonly IMapper _mapper;

    public ListExchangeRatesQueryHandler(IExchangeRateRepository rates, IMapper mapper)
    {
        _rates = rates;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<ExchangeRateDto>> Handle(ListExchangeRatesQuery request, CancellationToken cancellationToken) =>
        _mapper.Map<List<ExchangeRateDto>>((await _rates.ListAsync(cancellationToken: cancellationToken)).OrderBy(r => r.Currency));
}

public sealed record UpdateExchangeRateCommand(Currency Currency, decimal RateToTry) : ICommand<ExchangeRateDto>, IRequiresUser;

public sealed class UpdateExchangeRateCommandValidator : AbstractValidator<UpdateExchangeRateCommand>
{
    public UpdateExchangeRateCommandValidator()
    {
        RuleFor(x => x.Currency).IsInEnum();
        RuleFor(x => x.RateToTry).GreaterThan(0);
    }
}

public sealed class UpdateExchangeRateCommandHandler : IRequestHandler<UpdateExchangeRateCommand, ExchangeRateDto>
{
    private readonly IExchangeRateRepository _rates;
    private readonly IRepository<ExchangeRateChange> _rateChanges;
    private readonly IMapper _mapper;
    private readonly TimeProvider _time;
    private readonly ICurrentUser _currentUser;

    public UpdateExchangeRateCommandHandler(
        IExchangeRateRepository rates,
        IRepository<ExchangeRateChange> rateChanges,
        IMapper mapper,
        TimeProvider time,
        ICurrentUser currentUser)
    {
        _rates = rates;
        _rateChanges = rateChanges;
        _mapper = mapper;
        _time = time;
        _currentUser = currentUser;
    }

    public async Task<ExchangeRateDto> Handle(UpdateExchangeRateCommand request, CancellationToken cancellationToken)
    {
        var rate = await _rates.GetByIdAsync(request.Currency, cancellationToken)
                   ?? throw NotFoundException.ExchangeRate(request.Currency);

        var change = rate.Update(request.RateToTry, _currentUser.UserId!, _time.GetUtcNow());
        await _rateChanges.AddAsync(change, cancellationToken);

        return _mapper.Map<ExchangeRateDto>(rate);
    }
}

public sealed class ExchangeRateChangeDto
{
    public Guid Id { get; init; }
    public Currency Currency { get; init; }
    public decimal OldRateToTry { get; init; }
    public decimal NewRateToTry { get; init; }
    public string ChangedBy { get; init; } = default!;
    public DateTimeOffset ChangedAt { get; init; }
}

/// <summary>Exchange rate updates, newest first, optionally for one currency.</summary>
public sealed record GetExchangeRateHistoryQuery(Currency? Currency) : IQuery<IReadOnlyList<ExchangeRateChangeDto>>;

public sealed class GetExchangeRateHistoryQueryHandler : IRequestHandler<GetExchangeRateHistoryQuery, IReadOnlyList<ExchangeRateChangeDto>>
{
    private readonly IRepository<ExchangeRateChange> _rateChanges;
    private readonly IMapper _mapper;

    public GetExchangeRateHistoryQueryHandler(IRepository<ExchangeRateChange> rateChanges, IMapper mapper)
    {
        _rateChanges = rateChanges;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<ExchangeRateChangeDto>> Handle(GetExchangeRateHistoryQuery request, CancellationToken cancellationToken)
    {
        var changes = request.Currency is null
            ? await _rateChanges.ListAsync(cancellationToken: cancellationToken)
            : await _rateChanges.ListAsync(c => c.Currency == request.Currency, cancellationToken);

        return _mapper.Map<List<ExchangeRateChangeDto>>(changes.OrderByDescending(c => c.ChangedAt));
    }
}
