using AtmCash.Application.Abstractions.Messaging;
using AtmCash.Application.Abstractions.Persistence;
using AtmCash.Application.Common;
using AtmCash.Domain.Atms;
using AutoMapper;
using MediatR;

namespace AtmCash.Application.Features.Atms;

/// <summary>Cassettes, notes per denomination and total balance of one ATM.</summary>
public sealed record GetAtmQuery(Guid AtmId) : IQuery<AtmDto>;

public sealed class GetAtmQueryHandler : IRequestHandler<GetAtmQuery, AtmDto>
{
    private readonly IAtmRepository _atms;
    private readonly IMapper _mapper;

    public GetAtmQueryHandler(IAtmRepository atms, IMapper mapper)
    {
        _atms = atms;
        _mapper = mapper;
    }

    public async Task<AtmDto> Handle(GetAtmQuery request, CancellationToken cancellationToken)
    {
        var atm = await _atms.GetWithCassettesAsync(request.AtmId, cancellationToken)
                  ?? throw NotFoundException.Atm(request.AtmId);

        return _mapper.Map<AtmDto>(atm);
    }
}

public sealed record ListAtmsQuery : IQuery<IReadOnlyList<AtmDto>>;

public sealed class ListAtmsQueryHandler : IRequestHandler<ListAtmsQuery, IReadOnlyList<AtmDto>>
{
    private readonly IAtmRepository _atms;
    private readonly IMapper _mapper;

    public ListAtmsQueryHandler(IAtmRepository atms, IMapper mapper)
    {
        _atms = atms;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<AtmDto>> Handle(ListAtmsQuery request, CancellationToken cancellationToken)
    {
        var atms = await _atms.ListWithCassettesAsync(cancellationToken: cancellationToken);
        return _mapper.Map<List<AtmDto>>(atms);
    }
}

public sealed class AtmStatusChangeDto
{
    public Guid Id { get; init; }
    public AtmStatus FromStatus { get; init; }
    public AtmStatus ToStatus { get; init; }
    public string PerformedBy { get; init; } = default!;
    public DateTimeOffset OccurredAt { get; init; }
}

/// <summary>Status transitions of one ATM, newest first: who moved it from which status to which, and when.</summary>
public sealed record GetAtmStatusHistoryQuery(Guid AtmId) : IQuery<IReadOnlyList<AtmStatusChangeDto>>;

public sealed class GetAtmStatusHistoryQueryHandler : IRequestHandler<GetAtmStatusHistoryQuery, IReadOnlyList<AtmStatusChangeDto>>
{
    private readonly IAtmRepository _atms;
    private readonly IRepository<AtmStatusChange> _statusChanges;
    private readonly IMapper _mapper;

    public GetAtmStatusHistoryQueryHandler(IAtmRepository atms, IRepository<AtmStatusChange> statusChanges, IMapper mapper)
    {
        _atms = atms;
        _statusChanges = statusChanges;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<AtmStatusChangeDto>> Handle(GetAtmStatusHistoryQuery request, CancellationToken cancellationToken)
    {
        if (!await _atms.AnyAsync(a => a.Id == request.AtmId, cancellationToken))
        {
            throw NotFoundException.Atm(request.AtmId);
        }

        var changes = await _statusChanges.ListAsync(c => c.AtmId == request.AtmId, cancellationToken);
        return _mapper.Map<List<AtmStatusChangeDto>>(changes.OrderByDescending(c => c.OccurredAt));
    }
}
