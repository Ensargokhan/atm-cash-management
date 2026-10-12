using AtmCash.Application.Abstractions.Messaging;
using AtmCash.Application.Abstractions.Persistence;
using AtmCash.Application.Abstractions.Services;
using AtmCash.Application.Common;
using AutoMapper;
using MediatR;

namespace AtmCash.Application.Features.Atms;

public sealed record PutAtmInServiceCommand(Guid AtmId) : ICommand<AtmDto>, IRequiresUser;

public sealed class PutAtmInServiceCommandHandler : IRequestHandler<PutAtmInServiceCommand, AtmDto>
{
    private readonly IAtmRepository _atms;
    private readonly IMapper _mapper;
    private readonly TimeProvider _time;
    private readonly ICurrentUser _currentUser;

    public PutAtmInServiceCommandHandler(IAtmRepository atms, IMapper mapper, TimeProvider time, ICurrentUser currentUser)
    {
        _atms = atms;
        _mapper = mapper;
        _time = time;
        _currentUser = currentUser;
    }

    public async Task<AtmDto> Handle(PutAtmInServiceCommand request, CancellationToken cancellationToken)
    {
        var atm = await _atms.GetForUpdateAsync(request.AtmId, cancellationToken)
                  ?? throw NotFoundException.Atm(request.AtmId);

        atm.PutInService(_currentUser.UserId!, _time.GetUtcNow());
        return _mapper.Map<AtmDto>(atm);
    }
}

/// <summary>
/// Switches an in-service ATM to maintenance. Pending quotes are cancelled first so that their
/// reserved notes are released: reserved notes could otherwise be counted or unloaded during maintenance.
/// </summary>
public sealed record SwitchAtmToMaintenanceCommand(Guid AtmId) : ICommand<AtmDto>, IRequiresUser;

public sealed class SwitchAtmToMaintenanceCommandHandler : IRequestHandler<SwitchAtmToMaintenanceCommand, AtmDto>
{
    private readonly IAtmRepository _atms;
    private readonly IQuoteRepository _quotes;
    private readonly IMapper _mapper;
    private readonly TimeProvider _time;
    private readonly ICurrentUser _currentUser;

    public SwitchAtmToMaintenanceCommandHandler(IAtmRepository atms, IQuoteRepository quotes, IMapper mapper, TimeProvider time, ICurrentUser currentUser)
    {
        _atms = atms;
        _quotes = quotes;
        _mapper = mapper;
        _time = time;
        _currentUser = currentUser;
    }

    public async Task<AtmDto> Handle(SwitchAtmToMaintenanceCommand request, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var atm = await _atms.GetForUpdateAsync(request.AtmId, cancellationToken)
                  ?? throw NotFoundException.Atm(request.AtmId);

        foreach (var quote in await _quotes.ListPendingByAtmAsync(atm.Id, cancellationToken))
        {
            if (quote.IsExpiredAt(now))
            {
                quote.Expire(atm, now);
            }
            else
            {
                quote.Cancel(atm, now);
            }
        }

        atm.EnterMaintenance(_currentUser.UserId!, now);
        return _mapper.Map<AtmDto>(atm);
    }
}
