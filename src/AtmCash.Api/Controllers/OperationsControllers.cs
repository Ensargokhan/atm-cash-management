using AtmCash.Api.Contracts;
using AtmCash.Application.Features.Deposits;
using AtmCash.Application.Features.ExchangeRates;
using AtmCash.Application.Features.Ledger;
using AtmCash.Application.Features.Reconciliations;
using AtmCash.Application.Features.Reports;
using AtmCash.Domain.Common;
using AtmCash.Domain.Ledger;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using AtmCash.Api.Identity;


namespace AtmCash.Api.Controllers;

/// <summary>Customer deposits.</summary>
[ApiController]
[Produces("application/json")]
public sealed class DepositsController : ControllerBase
{
    private readonly ISender _sender;

    public DepositsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Places deposited notes: matching recycling cassette first, overflow to the deposit cassette,
    /// and returns notes that are unrecognised or do not fit.
    /// </summary>
    [HttpPost("api/atms/{atmId:guid}/deposits")]
    [ProducesResponseType<DepositResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<DepositResultDto> Deposit(Guid atmId, DepositRequest request, CancellationToken cancellationToken) =>
        await _sender.Send(new MakeDepositCommand(atmId, request.Notes ?? new(), request.UnrecognizedCount), cancellationToken);
}

/// <summary>Manual cash counts with four-eyes approval for large differences.</summary>
[ApiController]
[Produces("application/json")]
public sealed class ReconciliationsController : ControllerBase
{
    private readonly ISender _sender;

    public ReconciliationsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Submits a manual count (MAINTENANCE). Differences up to TRY 1,000 are applied immediately;
    /// above that the reconciliation awaits approval by a different X-User.
    /// </summary>
    [HttpPost("api/atms/{atmId:guid}/reconciliations")]
    [RequiresUser]
    [ProducesResponseType<ReconciliationDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReconciliationDto>> Submit(
        Guid atmId,
        SubmitReconciliationRequest request,
        CancellationToken cancellationToken)
    {
        var reconciliation = await _sender.Send(new SubmitReconciliationCommand(atmId, request.Counts ?? new()), cancellationToken);
        return CreatedAtAction(nameof(Get), new { reconciliationId = reconciliation.Id }, reconciliation);
    }

    [HttpGet("api/atms/{atmId:guid}/reconciliations")]
    public async Task<IReadOnlyList<ReconciliationDto>> List(Guid atmId, CancellationToken cancellationToken) =>
        await _sender.Send(new ListReconciliationsQuery(atmId), cancellationToken);

    [HttpGet("api/reconciliations/{reconciliationId:guid}")]
    [ProducesResponseType<ReconciliationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ReconciliationDto> Get(Guid reconciliationId, CancellationToken cancellationToken) =>
        await _sender.Send(new GetReconciliationQuery(reconciliationId), cancellationToken);

    /// <summary>Approves a reconciliation awaiting approval and applies its adjustments. X-User must differ from the submitter.</summary>
    [HttpPost("api/reconciliations/{reconciliationId:guid}/approve")]
    [RequiresUser]
    [ProducesResponseType<ReconciliationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ReconciliationDto> Approve(Guid reconciliationId, CancellationToken cancellationToken) =>
        await _sender.Send(new ReviewReconciliationCommand(reconciliationId, ReconciliationDecision.Approve), cancellationToken);


    /// <summary>Rejects a reconciliation awaiting approval; nothing is adjusted. X-User must differ from the submitter.</summary>
    [HttpPost("api/reconciliations/{reconciliationId:guid}/reject")]
    [RequiresUser]
    [ProducesResponseType<ReconciliationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ReconciliationDto> Reject(Guid reconciliationId, CancellationToken cancellationToken) =>
        await _sender.Send(new ReviewReconciliationCommand(reconciliationId, ReconciliationDecision.Reject), cancellationToken);

}

/// <summary>The append-only cassette movement ledger.</summary>
[ApiController]
[Produces("application/json")]
public sealed class LedgerController : ControllerBase
{
    private readonly ISender _sender;

    public LedgerController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Latest movements of an ATM, newest first.</summary>
    [HttpGet("api/atms/{atmId:guid}/movements")]
    [ProducesResponseType<IReadOnlyList<MovementDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IReadOnlyList<MovementDto>> Movements(
        Guid atmId,
        [FromQuery] int? slot,
        [FromQuery] MovementType? type,
        [FromQuery] int take = 200,
        CancellationToken cancellationToken = default) =>
        await _sender.Send(new GetMovementsQuery(atmId, slot, type, take), cancellationToken);

    /// <summary>Rebuilds the ATM's cassette balances from the ledger and reports differences with the stored balances.</summary>
    [HttpGet("api/atms/{atmId:guid}/ledger/consistency")]
    [ProducesResponseType<LedgerConsistencyReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<LedgerConsistencyReportDto> AtmConsistency(Guid atmId, CancellationToken cancellationToken) =>
        await _sender.Send(new CheckLedgerConsistencyQuery(atmId), cancellationToken);

    /// <summary>Consistency check for the whole network.</summary>
    [HttpGet("api/ledger/consistency")]
    public async Task<LedgerConsistencyReportDto> NetworkConsistency(CancellationToken cancellationToken) =>
        await _sender.Send(new CheckLedgerConsistencyQuery(null), cancellationToken);
}

[ApiController]
[Produces("application/json")]
public sealed class ReportsController : ControllerBase
{
    private readonly ISender _sender;

    public ReportsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Per ATM and cassette: note, reserved and available counts; totals per currency and overall in TRY.</summary>
    [HttpGet("api/reports/operations")]
    [ProducesResponseType<OperationsReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<OperationsReportDto> Operations([FromQuery] Guid? atmId, CancellationToken cancellationToken) =>
        await _sender.Send(new GetOperationsReportQuery(atmId), cancellationToken);
}

[ApiController]
[Route("api/exchange-rates")]
[Produces("application/json")]
public sealed class ExchangeRatesController : ControllerBase
{
    private readonly ISender _sender;

    public ExchangeRatesController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    public async Task<IReadOnlyList<ExchangeRateDto>> List(CancellationToken cancellationToken) =>
        await _sender.Send(new ListExchangeRatesQuery(), cancellationToken);

    /// <summary>Updates a rate; the change is kept in the rate history with the X-User who made it.</summary>
    [HttpPut("{currency}")]
    [RequiresUser]
    [ProducesResponseType<ExchangeRateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ExchangeRateDto> Update(Currency currency, UpdateExchangeRateRequest request, CancellationToken cancellationToken) =>
        await _sender.Send(new UpdateExchangeRateCommand(currency, request.RateToTry), cancellationToken);

    /// <summary>Rate updates, newest first, optionally for one currency: old and new rate, who changed it and when.</summary>
    [HttpGet("history")]
    [ProducesResponseType<IReadOnlyList<ExchangeRateChangeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IReadOnlyList<ExchangeRateChangeDto>> History([FromQuery] Currency? currency, CancellationToken cancellationToken) =>
        await _sender.Send(new GetExchangeRateHistoryQuery(currency), cancellationToken);
}
