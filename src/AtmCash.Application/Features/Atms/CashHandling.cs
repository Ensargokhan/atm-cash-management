using AtmCash.Application.Abstractions.Messaging;
using AtmCash.Application.Abstractions.Persistence;
using AtmCash.Application.Common;
using AtmCash.Domain.Atms;
using AutoMapper;
using FluentValidation;
using MediatR;
using AtmCash.Application.Abstractions.Services;


namespace AtmCash.Application.Features.Atms;

// ---------------------------------------------------------------- Load

public sealed record CashLoadInput(int Slot, int Count);

/// <summary>Records cash put into recycling cassettes by hand.</summary>
public sealed record LoadCashCommand(Guid AtmId, IReadOnlyList<CashLoadInput> Items) : ICommand<AtmDto>, IRequiresUser;

public sealed class LoadCashCommandValidator : AbstractValidator<LoadCashCommand>
{
    public LoadCashCommandValidator()
    {
        RuleFor(x => x.Items).NotEmpty()
            .Must(items => items.Select(i => i.Slot).Distinct().Count() == items.Count)
            .WithMessage("Each cassette may appear only once.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Slot).InclusiveBetween(1, Atm.CassetteSlots);
            item.RuleFor(i => i.Count).InclusiveBetween(1, Cassette.DefaultCapacity);
        });
    }
}

public sealed class LoadCashCommandHandler : IRequestHandler<LoadCashCommand, AtmDto>
{
    private readonly IAtmRepository _atms;
    private readonly IMapper _mapper;
    private readonly TimeProvider _time;
    private readonly ICurrentUser _currentUser;

    public LoadCashCommandHandler(IAtmRepository atms, IMapper mapper, TimeProvider time, ICurrentUser currentUser)
    {
        _atms = atms;
        _mapper = mapper;
        _time = time;
        _currentUser = currentUser;

    }

    public async Task<AtmDto> Handle(LoadCashCommand request, CancellationToken cancellationToken)
    {
        var atm = await _atms.GetForUpdateAsync(request.AtmId, cancellationToken)
                  ?? throw NotFoundException.Atm(request.AtmId);

        atm.LoadCash(request.Items.Select(i => new CashLoadItem(i.Slot, i.Count)).ToList(), _currentUser.UserId!, _time.GetUtcNow());
        return _mapper.Map<AtmDto>(atm);
    }
}

// ---------------------------------------------------------------- Unload

/// <summary>Empties cassettes during maintenance (e.g. removing deposited cash).</summary>
public sealed record UnloadCassettesCommand(Guid AtmId, IReadOnlyList<int> Slots) : ICommand<UnloadResultDto>, IRequiresUser;

public sealed class UnloadCassettesCommandValidator : AbstractValidator<UnloadCassettesCommand>
{
    public UnloadCassettesCommandValidator()
    {
        RuleFor(x => x.Slots).NotEmpty();
        RuleForEach(x => x.Slots).InclusiveBetween(1, Atm.CassetteSlots);
    }
}

public sealed class UnloadCassettesCommandHandler : IRequestHandler<UnloadCassettesCommand, UnloadResultDto>
{
    private readonly IAtmRepository _atms;
    private readonly IMapper _mapper;
    private readonly TimeProvider _time;
    private readonly ICurrentUser _currentUser;

    public UnloadCassettesCommandHandler(IAtmRepository atms, IMapper mapper, TimeProvider time, ICurrentUser currentUser)
    {
        _atms = atms;
        _mapper = mapper;
        _time = time;
        _currentUser = currentUser;
    }


    public async Task<UnloadResultDto> Handle(UnloadCassettesCommand request, CancellationToken cancellationToken)
    {
        var atm = await _atms.GetForUpdateAsync(request.AtmId, cancellationToken)
                  ?? throw NotFoundException.Atm(request.AtmId);

        var unloaded = atm.UnloadCassettes(request.Slots, _currentUser.UserId, _time.GetUtcNow());
        return new UnloadResultDto(_mapper.Map<AtmDto>(atm), _mapper.Map<List<CurrencyNotesDto>>(unloaded));
    }
}
