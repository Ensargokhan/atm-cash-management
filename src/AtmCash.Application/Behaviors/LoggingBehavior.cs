using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AtmCash.Application.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        var watch = Stopwatch.StartNew();
        try
        {
            var response = await next();
            _logger.LogInformation("{Request} handled in {ElapsedMs} ms", name, watch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("{Request} failed after {ElapsedMs} ms: {Error}", name, watch.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }
}
