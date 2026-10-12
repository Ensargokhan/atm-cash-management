using AtmCash.Api.Contracts;
using AtmCash.Application.Features.Withdrawals;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using AtmCash.Application.Behaviors;

namespace AtmCash.Api.Controllers;

/// <summary>Two-step customer withdrawal: quote (reserve notes for 60 s), then confirm or cancel.</summary>
[ApiController]
[Produces("application/json")]
public sealed class WithdrawalsController : ControllerBase
{
    private readonly ISender _sender;

    public WithdrawalsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Finds the fewest-notes breakdown for the amount, or the closest lower/upper alternatives,
    /// and reserves the offered notes for 60 seconds. Checks the card's daily limit.
    /// </summary>
    [HttpPost("api/atms/{atmId:guid}/withdrawal-quotes")]
    [ProducesResponseType<QuoteDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<QuoteDto>> CreateQuote(Guid atmId, CreateQuoteRequest request, CancellationToken cancellationToken)
    {
        var quote = await _sender.Send(new CreateQuoteCommand(atmId, request.CardNumber, request.Currency, request.Amount), cancellationToken);
        return CreatedAtAction(nameof(GetQuote), new { quoteId = quote.QuoteId }, quote);
    }

    [HttpGet("api/withdrawal-quotes/{quoteId:guid}")]
    [ProducesResponseType<QuoteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<QuoteDto> GetQuote(Guid quoteId, CancellationToken cancellationToken) =>
        await _sender.Send(new GetQuoteQuery(quoteId), cancellationToken);

    /// <summary>
    /// Confirms the quote and debits the cash. Requires an <c>Idempotency-Key</c> header:
    /// repeating the request with the same key returns the same result without debiting again.
    /// </summary>
    [HttpPost("api/withdrawal-quotes/{quoteId:guid}/confirm")]
    [ProducesResponseType<ConfirmQuoteResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ConfirmQuoteResult> Confirm(
        Guid quoteId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ConfirmQuoteRequest? request,
        [FromServices] IdempotencyContext idempotency,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new ConfirmQuoteCommand(quoteId, request?.Option, idempotencyKey ?? string.Empty), cancellationToken);

        if (idempotency.IsReplayed)
        {
            Response.Headers["Idempotent-Replayed"] = "true";
        }

        return result;
    }


    /// <summary>Cancels a pending quote and releases its notes.</summary>
    [HttpPost("api/withdrawal-quotes/{quoteId:guid}/cancel")]
    [ProducesResponseType<QuoteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<QuoteDto> Cancel(Guid quoteId, CancellationToken cancellationToken) =>
        await _sender.Send(new CancelQuoteCommand(quoteId), cancellationToken);
}
