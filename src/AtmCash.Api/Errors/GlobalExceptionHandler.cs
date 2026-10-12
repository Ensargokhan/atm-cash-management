using AtmCash.Application.Common;
using AtmCash.Domain.Common;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AtmCash.Api.Errors;

/// <summary>
/// Turns every exception into an RFC 7807 ProblemDetails response with a stable <c>errorCode</c> extension.
///   400 validation / malformed request · 404 not found · 409 wrong state or concurrent change
///   422 business rule violated · 500 unexpected
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ValidationException validation => Validation(validation),
            BadRequestException badRequest => Create(StatusCodes.Status400BadRequest, "Bad request", badRequest.ErrorCode, badRequest.Message),
            NotFoundException notFound => Create(StatusCodes.Status404NotFound, "Not found", notFound.ErrorCode, notFound.Message),
            InvalidStateException state => Create(StatusCodes.Status409Conflict, "Conflict", state.ErrorCode, state.Message),
            DomainException domain => Create(StatusCodes.Status422UnprocessableEntity, "Business rule violated", domain.ErrorCode, domain.Message),
            DbUpdateException => Create(StatusCodes.Status409Conflict, "Conflict", ErrorCodes.ConcurrencyConflict,
                "The data was changed by another request. Please retry."),
            _ => null
        };

        if (problem is null)
        {
            _logger.LogError(exception, "Unhandled exception");
            problem = Create(StatusCodes.Status500InternalServerError, "Internal server error", ErrorCodes.InternalError,
                "An unexpected error occurred.");
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static ProblemDetails Create(int status, string title, string errorCode, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.io/{status}"
        };
        problem.Extensions["errorCode"] = errorCode;
        return problem;
    }

    private static ProblemDetails Validation(ValidationException exception)
    {
        // A single custom code (e.g. IDEMPOTENCY_KEY_REQUIRED) is more useful to clients than the generic one.
        var customCodes = exception.Errors
            .Select(e => e.ErrorCode)
            .Where(c => !string.IsNullOrEmpty(c) && !c.EndsWith("Validator", StringComparison.Ordinal))
            .Distinct()
            .ToList();
        var errorCode = customCodes.Count == 1 && exception.Errors.All(e => e.ErrorCode == customCodes[0])
            ? customCodes[0]
            : ErrorCodes.ValidationFailed;

        var problem = new ValidationProblemDetails(
            exception.Errors
                .GroupBy(e => string.IsNullOrEmpty(e.PropertyName) ? "request" : e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
            Detail = string.Join(" ", exception.Errors.Select(e => e.ErrorMessage).Distinct()),
            Type = "https://httpstatuses.io/400"
        };
        problem.Extensions["errorCode"] = errorCode;
        return problem;
    }
}
