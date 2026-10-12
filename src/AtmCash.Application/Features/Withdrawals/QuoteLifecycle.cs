using AtmCash.Application.Abstractions.Messaging;
using AtmCash.Application.Abstractions.Persistence;
using AtmCash.Application.Common;
using AutoMapper;
using MediatR;

namespace AtmCash.Application.Features.Withdrawals;

// ---------------------------------------------------------------- Cancel

public sealed record CancelQuoteCommand(Guid QuoteId) : ICommand<QuoteDto>;

public sealed class CancelQuoteCommandHandler : IRequestHandler<CancelQuoteCommand, QuoteDto>
{
    private readonly IAtmRepository _atms;
    private readonly IQuoteRepository _quotes;
    private readonly IMapper _mapper;
    private readonly TimeProvider _time;

    public CancelQuoteCommandHandler(IAtmRepository atms, IQuoteRepository quotes, IMapper mapper, TimeProvider time)
    {
        _atms = atms;
        _quotes = quotes;
        _mapper = mapper;
        _time = time;
    }

    public async Task<QuoteDto> Handle(CancelQuoteCommand request, CancellationToken cancellationToken)
    {
        var atmId = await _quotes.GetAtmIdAsync(request.QuoteId, cancellationToken)
                    ?? throw NotFoundException.Quote(request.QuoteId);
        var atm = await _atms.GetForUpdateAsync(atmId, cancellationToken)
                  ?? throw NotFoundException.Atm(atmId);
        var quote = await _quotes.GetByIdAsync(request.QuoteId, cancellationToken)
                    ?? throw NotFoundException.Quote(request.QuoteId);

        quote.Cancel(atm, _time.GetUtcNow());
        return _mapper.Map<QuoteDto>(quote);
    }
}

// ---------------------------------------------------------------- Expire (background)

/// <summary>Releases the reservations of the ATM's expired quotes. Sent by the quote expiry background service.</summary>
public sealed record ExpireQuotesCommand(Guid AtmId) : ICommand<int>;

public sealed class ExpireQuotesCommandHandler : IRequestHandler<ExpireQuotesCommand, int>
{
    private readonly IAtmRepository _atms;
    private readonly IQuoteRepository _quotes;
    private readonly TimeProvider _time;

    public ExpireQuotesCommandHandler(IAtmRepository atms, IQuoteRepository quotes, TimeProvider time)
    {
        _atms = atms;
        _quotes = quotes;
        _time = time;
    }

    public async Task<int> Handle(ExpireQuotesCommand request, CancellationToken cancellationToken)
    {
        var atm = await _atms.GetForUpdateAsync(request.AtmId, cancellationToken);
        if (atm is null)
        {
            return 0;
        }

        return await QuoteHousekeeping.ExpireStaleQuotesAsync(atm, _quotes, _time.GetUtcNow(), cancellationToken);
    }
}

// ---------------------------------------------------------------- Get

public sealed record GetQuoteQuery(Guid QuoteId) : IQuery<QuoteDto>;

public sealed class GetQuoteQueryHandler : IRequestHandler<GetQuoteQuery, QuoteDto>
{
    private readonly IQuoteRepository _quotes;
    private readonly IMapper _mapper;

    public GetQuoteQueryHandler(IQuoteRepository quotes, IMapper mapper)
    {
        _quotes = quotes;
        _mapper = mapper;
    }

    public async Task<QuoteDto> Handle(GetQuoteQuery request, CancellationToken cancellationToken)
    {
        var quote = await _quotes.GetByIdAsync(request.QuoteId, cancellationToken)
                    ?? throw NotFoundException.Quote(request.QuoteId);

        return _mapper.Map<QuoteDto>(quote);
    }
}
