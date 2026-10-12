using AtmCash.Application.Abstractions.Messaging;
using AtmCash.Application.Abstractions.Persistence;
using AtmCash.Application.Abstractions.Services;
using AtmCash.Application.Common;
using AtmCash.Domain.Atms;
using AtmCash.Domain.Common;
using AtmCash.Domain.Withdrawals;
using AutoMapper;
using FluentValidation;
using MediatR;

namespace AtmCash.Application.Features.Withdrawals;

/// <summary>Step 1 of a withdrawal: find the notes to pay the amount and reserve them for 60 seconds.</summary>
public sealed record CreateQuoteCommand(Guid AtmId, string CardNumber, Currency Currency, int Amount) : ICommand<QuoteDto>;

public sealed class CreateQuoteCommandValidator : AbstractValidator<CreateQuoteCommand>
{
    public CreateQuoteCommandValidator()
    {
        RuleFor(x => x.CardNumber).NotEmpty().Matches(@"^\d{12,19}$").WithMessage("Card number must be 12 to 19 digits.");
        RuleFor(x => x.Currency).IsInEnum();
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(1_000_000);
    }
}

public sealed class CreateQuoteCommandHandler : IRequestHandler<CreateQuoteCommand, QuoteDto>
{
    private readonly IAtmRepository _atms;
    private readonly IQuoteRepository _quotes;
    private readonly ICardHasher _cardHasher;
    private readonly IResourceLock _resourceLock;
    private readonly IMapper _mapper;
    private readonly TimeProvider _time;

    public CreateQuoteCommandHandler(
        IAtmRepository atms,
        IQuoteRepository quotes,
        ICardHasher cardHasher,
        IResourceLock resourceLock,
        IMapper mapper,
        TimeProvider time)
    {
        _atms = atms;
        _quotes = quotes;
        _cardHasher = cardHasher;
        _resourceLock = resourceLock;
        _mapper = mapper;
        _time = time;
    }

    public async Task<QuoteDto> Handle(CreateQuoteCommand request, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();

        // 1. Serialise all cash operations on this ATM: concurrent quotes see each other's reservations.
        var atm = await _atms.GetForUpdateAsync(request.AtmId, cancellationToken)
                  ?? throw NotFoundException.Atm(request.AtmId);

        if (atm.Status != AtmStatus.InService)
        {
            throw new InvalidStateException(ErrorCodes.OperationNotAllowedInState,
                $"Withdrawals are only allowed while the ATM is {AtmStatus.InService}; it is {atm.Status}.");
        }

        // 2. Free notes held by quotes that have already expired, so they can be offered again.
        await QuoteHousekeeping.ExpireStaleQuotesAsync(atm, _quotes, now, cancellationToken);

        // 3. Serialise quotes for the same card and currency across ATMs, so the daily limit cannot be overrun.
        var cardHash = _cardHasher.Hash(request.CardNumber);
        await _resourceLock.AcquireAsync($"card-limit:{cardHash}:{request.Currency}", cancellationToken);

        var limit = DailyWithdrawalLimits.For(request.Currency);
        var used = await _quotes.GetUsedDailyAmountAsync(cardHash, request.Currency, BusinessDay.StartOfDay(now), now, cancellationToken);
        var remaining = Math.Max(0, limit - used);

        if (request.Amount > remaining)
        {
            throw new DomainException(ErrorCodes.DailyLimitExceeded,
                $"Requested {request.Amount} {request.Currency} exceeds the remaining daily limit of {remaining} {request.Currency} " +
                $"(limit {limit}, used or reserved {used}).");
        }

        // 4. Fewest-notes breakdown, or lower/upper alternatives (upper only within the remaining limit).
        var selection = NoteSelector.Select(atm.AvailableNotes(request.Currency), request.Amount, remaining);

        // 5. Create the quote and reserve the offered notes.
        var quote = Quote.Create(atm, cardHash, request.Currency, selection, now);
        await _quotes.AddAsync(quote, cancellationToken);

        var dto = _mapper.Map<QuoteDto>(quote);
        dto.RemainingDailyLimit = remaining - quote.ReservedAmount;
        return dto;
    }
}
