using AtmCash.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.MsSql;

namespace AtmCash.IntegrationTests.Infrastructure;

/// <summary>
/// Runs the real API (all layers, real SQL Server in a container, migrations applied on startup).
/// Time is controlled by a <see cref="FakeTimeProvider"/> so quote expiry can be tested without waiting.
/// </summary>
public sealed class AtmApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _database = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connectionString = new SqlConnectionStringBuilder(_database.GetConnectionString())
        {
            InitialCatalog = "AtmCashTests"
        }.ConnectionString;

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("CardHashing:Pepper", "integration-test-pepper-0123456789-abcdef");
        builder.UseSetting("BackgroundJobs:OutboxIntervalSeconds", "1");

        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
    }

    public async Task<T> WithDbContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<AtmApiFactory>
{
    public const string Name = "api";
}
