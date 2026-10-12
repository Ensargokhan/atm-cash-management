using AtmCash.Application.Abstractions.Persistence;
using AtmCash.Application.Abstractions.Services;
using AtmCash.Infrastructure.BackgroundJobs;
using AtmCash.Infrastructure.Persistence;
using AtmCash.Infrastructure.Persistence.Repositories;
using AtmCash.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace AtmCash.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
                               ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        // No EnableRetryOnFailure: retrying strategies do not support user-initiated transactions,
        // and every command here runs in an explicit transaction (TransactionBehavior).
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IAtmRepository, AtmRepository>();
        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<IReconciliationRepository, ReconciliationRepository>();
        services.AddScoped<IExchangeRateRepository, ExchangeRateRepository>();
        services.AddScoped<IMovementRepository, MovementRepository>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IResourceLock, SqlServerResourceLock>();

        services.Configure<CardHashingOptions>(configuration.GetSection(CardHashingOptions.SectionName));
        services.AddSingleton<ICardHasher, HmacCardHasher>();

        services.TryAddSingleton(TimeProvider.System);

        services.Configure<BackgroundJobOptions>(configuration.GetSection(BackgroundJobOptions.SectionName));
        if (configuration.GetValue($"{BackgroundJobOptions.SectionName}:Enabled", true))
        {
            services.AddHostedService<OutboxProcessor>();
            services.AddHostedService<QuoteExpiryService>();
        }

        return services;
    }

    /// <summary>Applies pending migrations, retrying while the database container is still starting.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, ILogger logger, CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 30;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync(cancellationToken);
                logger.LogInformation("Database migrations applied");
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning("Database not ready (attempt {Attempt}/{Max}): {Error}", attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }
}
