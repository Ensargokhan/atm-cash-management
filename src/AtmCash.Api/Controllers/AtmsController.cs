using AtmCash.Api.Contracts;
using AtmCash.Application.Features.Atms;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using AtmCash.Api.Identity;


namespace AtmCash.Api.Controllers;

/// <summary>ATM setup, lifecycle and cash handling by operations staff.</summary>
[ApiController]
[Route("api/atms")]
[Produces("application/json")]
public sealed class AtmsController : ControllerBase
{
    private readonly ISender _sender;

    public AtmsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Registers a new ATM (state UNCONFIGURED).</summary>
    [HttpPost]
    [ProducesResponseType<AtmDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AtmDto>> Create(CreateAtmRequest request, CancellationToken cancellationToken)
    {
        var atm = await _sender.Send(new CreateAtmCommand(request.Code, request.Name ?? request.Code, request.Location), cancellationToken);
        return CreatedAtAction(nameof(Get), new { atmId = atm.Id }, atm);
    }

    [HttpGet]
    public async Task<IReadOnlyList<AtmDto>> List(CancellationToken cancellationToken) =>
        await _sender.Send(new ListAtmsQuery(), cancellationToken);

    /// <summary>Cassettes, notes per denomination and total balance per currency.</summary>
    [HttpGet("{atmId:guid}")]
    [ProducesResponseType<AtmDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<AtmDto> Get(Guid atmId, CancellationToken cancellationToken) =>
        await _sender.Send(new GetAtmQuery(atmId), cancellationToken);

    /// <summary>Configures the five cassettes. Allowed in UNCONFIGURED, CONFIGURED, LOADED and MAINTENANCE.</summary>
    [HttpPut("{atmId:guid}/cassettes")]
    [RequiresUser]
    [ProducesResponseType<AtmDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<AtmDto> ConfigureCassettes(Guid atmId, ConfigureCassettesRequest request, CancellationToken cancellationToken) =>
        await _sender.Send(new ConfigureCassettesCommand(atmId, request.Cassettes), cancellationToken);

    /// <summary>Records cash loaded by hand into recycling cassettes. CONFIGURED → LOADED on the first load.</summary>
    [HttpPost("{atmId:guid}/cash-loads")]
    [RequiresUser]
    [ProducesResponseType<AtmDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<AtmDto> LoadCash(Guid atmId, LoadCashRequest request, CancellationToken cancellationToken) =>
        await _sender.Send(new LoadCashCommand(atmId, request.Items), cancellationToken);

    /// <summary>Empties cassettes (MAINTENANCE only), e.g. to collect deposited cash.</summary>
    [HttpPost("{atmId:guid}/cash-unloads")]
    [RequiresUser]
    [ProducesResponseType<UnloadResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<UnloadResultDto> UnloadCassettes(Guid atmId, UnloadCassettesRequest request, CancellationToken cancellationToken) =>
        await _sender.Send(new UnloadCassettesCommand(atmId, request.Slots), cancellationToken);

    /// <summary>LOADED or MAINTENANCE → IN_SERVICE. Rejected while a reconciliation awaits approval.</summary>
    [HttpPost("{atmId:guid}/put-in-service")]
    [RequiresUser]
    [ProducesResponseType<AtmDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<AtmDto> PutInService(Guid atmId, CancellationToken cancellationToken) =>
        await _sender.Send(new PutAtmInServiceCommand(atmId), cancellationToken);

    /// <summary>IN_SERVICE → MAINTENANCE. Pending quotes are cancelled and their notes released.</summary>
    [HttpPost("{atmId:guid}/maintenance")]
    [RequiresUser]
    [ProducesResponseType<AtmDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<AtmDto> SwitchToMaintenance(Guid atmId, CancellationToken cancellationToken) =>
        await _sender.Send(new SwitchAtmToMaintenanceCommand(atmId), cancellationToken);

    /// <summary>Status transitions of the ATM, newest first: who changed the status, from what to what, and when.</summary>
    [HttpGet("{atmId:guid}/status-history")]
    [ProducesResponseType<IReadOnlyList<AtmStatusChangeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IReadOnlyList<AtmStatusChangeDto>> StatusHistory(Guid atmId, CancellationToken cancellationToken) =>
        await _sender.Send(new GetAtmStatusHistoryQuery(atmId), cancellationToken);
}
