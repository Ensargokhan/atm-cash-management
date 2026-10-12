using AtmCash.Application.Abstractions.Messaging;
using AtmCash.Application.Abstractions.Persistence;
using AtmCash.Application.Common;
using AtmCash.Application.Features.Atms;
using AtmCash.Domain.Common;
using AtmCash.Domain.Withdrawals;
using FluentValidation;
using MediatR;

namespace AtmCash.Application.Features.Withdrawals;

/// <summary>
/// Step 2 of a withdrawal: the customer accepts one of the offered options and the cash is debited.
/// Idempotent: a retry with the same Idempotency-Key returns the stored result and debits nothing.
/// </summary>
/// <param name="Option">Which option to pay. May be omitted when the quote has an exact match.</param>
public sealed record ConfirmQuoteCommand(Guid QuoteId, QuoteOptionKind? Option, string IdempotencyKey)
    : ICommand<ConfirmQuoteResult>, IIdempotentCommand
{
    public string IdempotencyScope => "confirm-quote";

    public string RequestFingerprint => $"{QuoteId:N}:{Option?.ToString() ?? "default"}";
}

public sealed class ConfirmQuoteCommandValidator : AbstractValidator<ConfirmQuoteCommand>
{
    public ConfirmQuoteCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty().WithErrorCode(ErrorCodes.IdempotencyKeyRequired)
            .WithMessage("The Idempotency-Key header is required.")
            .MaximumLength(100);
        RuleFor(x => x.Option).IsInEnum();
    }
}

public sealed class ConfirmQuoteCommandHandler : IRequestHandler<ConfirmQuoteCommand, ConfirmQuoteResult>
{
    private readonly IAtmRepository _atms;
    private readonly IQuoteRepository _quotes;
    private readonly TimeProvider _time;

    public ConfirmQuoteCommandHandler(IAtmRepository atms, IQuoteRepository quotes, TimeProvider time)
    {
        _atms = atms;
        _quotes = quotes;
        _time = time;
    }

    public async Task<ConfirmQuoteResult> Handle(ConfirmQuoteCommand request, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();

        // Lock the ATM first, then read the quote, so its status cannot change underneath us.
        var atmId = await _quotes.GetAtmIdAsync(request.QuoteId, cancellationToken)
                    ?? throw NotFoundException.Quote(request.QuoteId);
        var atm = await _atms.GetForUpdateAsync(atmId, cancellationToken)
                  ?? throw NotFoundException.Atm(atmId);
        var quote = await _quotes.GetByIdAsync(request.QuoteId, cancellationToken)
                    ?? throw NotFoundException.Quote(request.QuoteId);

        var kind = request.Option
                   ?? (quote.IsExactMatch
                       ? QuoteOptionKind.Exact
                       : throw new DomainException(ErrorCodes.QuoteOptionNotAvailable,
                           "This quote has no exact match; choose the Lower or Upper option."));

        var confirmation = quote.Confirm(atm, kind, now);

        return new ConfirmQuoteResult(
            quote.Id,
            atm.Id,
            quote.Status,
            kind,
            quote.Currency,
            confirmation.Option.Amount,
            confirmation.Option.Notes.Select(n => new NoteDto(n.Denomination, n.Count)).ToList(),
            confirmation.Dispensed.Select(d => new DispensedFromCassetteDto(d.Slot, d.Denomination, d.Count)).ToList(),
            now);
    }
}
