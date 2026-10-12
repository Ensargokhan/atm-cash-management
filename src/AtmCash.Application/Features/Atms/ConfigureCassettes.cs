using AtmCash.Application.Abstractions.Messaging;
using AtmCash.Application.Abstractions.Persistence;
using AtmCash.Application.Common;
using AtmCash.Domain.Atms;
using AtmCash.Domain.Common;
using AutoMapper;
using FluentValidation;
using MediatR;
using AtmCash.Application.Abstractions.Services;


namespace AtmCash.Application.Features.Atms;

public sealed record CassetteConfigurationInput(int Slot, CassetteType Type, Currency? Currency, int? Denomination);

public sealed record ConfigureCassettesCommand(Guid AtmId, IReadOnlyList<CassetteConfigurationInput> Cassettes) : ICommand<AtmDto>, IRequiresUser;

public sealed class ConfigureCassettesCommandValidator : AbstractValidator<ConfigureCassettesCommand>
{
    public ConfigureCassettesCommandValidator()
    {
        RuleFor(x => x.Cassettes).NotNull()
            .Must(c => c.Count == Atm.CassetteSlots).WithMessage($"Exactly {Atm.CassetteSlots} cassettes must be configured.");

        RuleForEach(x => x.Cassettes).ChildRules(cassette =>
        {
            cassette.RuleFor(c => c.Slot).InclusiveBetween(1, Atm.CassetteSlots);
            cassette.RuleFor(c => c.Type).IsInEnum();
            cassette.RuleFor(c => c.Currency).IsInEnum();
            cassette.RuleFor(c => c.Denomination)
                .Must(d => d is null || Denominations.IsSupported(d.Value))
                .WithMessage($"Denomination must be one of {string.Join(", ", Denominations.All)}.");
        });
    }
}

public sealed class ConfigureCassettesCommandHandler : IRequestHandler<ConfigureCassettesCommand, AtmDto>
{
    private readonly IAtmRepository _atms;
    private readonly IMapper _mapper;
    private readonly TimeProvider _time;
    private readonly ICurrentUser _currentUser;

    public ConfigureCassettesCommandHandler(IAtmRepository atms, IMapper mapper, TimeProvider time, ICurrentUser currentUser)
    {
        _atms = atms;
        _mapper = mapper;
        _time = time;
        _currentUser = currentUser;
    }


    public async Task<AtmDto> Handle(ConfigureCassettesCommand request, CancellationToken cancellationToken)
    {
        var atm = await _atms.GetForUpdateAsync(request.AtmId, cancellationToken)
                  ?? throw NotFoundException.Atm(request.AtmId);

        var configurations = request.Cassettes
            .Select(c => new CassetteConfiguration(c.Slot, c.Type, c.Currency, c.Denomination))
            .ToList();

        atm.ConfigureCassettes(configurations, _currentUser.UserId!, _time.GetUtcNow());
        return _mapper.Map<AtmDto>(atm);
    }
}
