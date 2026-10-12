using AtmCash.Application.Abstractions.Messaging;
using AtmCash.Application.Abstractions.Persistence;
using AtmCash.Domain.Atms;
using AtmCash.Domain.Common;
using AutoMapper;
using FluentValidation;
using MediatR;

namespace AtmCash.Application.Features.Atms;

public sealed record CreateAtmCommand(string Code, string Name, string? Location) : ICommand<AtmDto>;

public sealed class CreateAtmCommandValidator : AbstractValidator<CreateAtmCommand>
{
    public CreateAtmCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).MaximumLength(100);
        RuleFor(x => x.Location).MaximumLength(200);
    }
}

public sealed class CreateAtmCommandHandler : IRequestHandler<CreateAtmCommand, AtmDto>
{
    private readonly IAtmRepository _atms;
    private readonly IMapper _mapper;
    private readonly TimeProvider _time;

    public CreateAtmCommandHandler(IAtmRepository atms, IMapper mapper, TimeProvider time)
    {
        _atms = atms;
        _mapper = mapper;
        _time = time;
    }

    public async Task<AtmDto> Handle(CreateAtmCommand request, CancellationToken cancellationToken)
    {
        if (await _atms.CodeExistsAsync(request.Code.Trim(), cancellationToken))
        {
            throw new InvalidStateException(ErrorCodes.AtmCodeAlreadyExists, $"An ATM with code '{request.Code}' already exists.");
        }

        var atm = Atm.Create(request.Code, request.Name, request.Location, _time.GetUtcNow());
        await _atms.AddAsync(atm, cancellationToken);
        return _mapper.Map<AtmDto>(atm);
    }
}
