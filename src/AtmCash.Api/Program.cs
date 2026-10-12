using System.Reflection;
using System.Text.Json.Serialization;
using AtmCash.Api.Errors;
using AtmCash.Application;
using AtmCash.Domain.Common;
using AtmCash.Infrastructure;
using AtmCash.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using AtmCash.Api.Identity;
using AtmCash.Application.Abstractions.Services;
using AtmCash.Api.Swagger;
using System.Text;



var builder = WebApplication.CreateBuilder(args);


// Kestrel rejects non-ASCII header values with an empty 400 before the request reaches the application.
// Decode X-User as Latin-1 instead, so a value such as "Gökhan" reaches CurrentUserBehavior
// and is rejected with a proper INVALID_USER ProblemDetails. Other headers keep the default (ASCII only).
builder.WebHost.ConfigureKestrel(options =>
    options.RequestHeaderEncodingSelector = headerName =>
        string.Equals(headerName, HeaderCurrentUser.HeaderName, StringComparison.OrdinalIgnoreCase)
            ? Encoding.Latin1
            : null);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);


builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HeaderCurrentUser>();


builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .ConfigureApiBehaviorOptions(options =>
    {
        // Malformed bodies / model binding errors use the same ProblemDetails shape and error code.
        options.InvalidModelStateResponseFactory = context =>
        {
            var problem = new ValidationProblemDetails(context.ModelState)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed",
                Type = "https://httpstatuses.io/400"
            };
            problem.Extensions["errorCode"] = ErrorCodes.ValidationFailed;
            return new BadRequestObjectResult(problem);
        };
    });

builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    });
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ATM Cash Management API",
        Version = "v1",
        Description = "Cash management backend for an ATM network: cassettes, withdrawals (quote/confirm), deposits, " +
                      "reconciliation with four-eyes approval, movement ledger and reporting. " +
                      "Errors are RFC 7807 ProblemDetails with an `errorCode` extension."
    });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml))
    {
        options.IncludeXmlComments(xml);
    }

    options.OperationFilter<UserHeaderOperationFilter>();

});

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    await app.Services.MigrateDatabaseAsync(app.Logger);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseSwagger();
app.UseSwaggerUI(options => options.DocumentTitle = "ATM Cash Management API");

app.MapControllers();
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

/// <summary>Entry point; public so integration tests can use WebApplicationFactory.</summary>
public partial class Program;
