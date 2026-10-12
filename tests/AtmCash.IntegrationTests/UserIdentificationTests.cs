using System.Net;
using AtmCash.IntegrationTests.Infrastructure;

namespace AtmCash.IntegrationTests;

/// <summary>Staff operations require a valid X-User; the user is recorded in the ledger.</summary>
[Collection(ApiCollection.Name)]
public sealed class UserIdentificationTests
{
    private readonly ApiClient _api;

    public UserIdentificationTests(AtmApiFactory factory)
    {
        _api = new ApiClient(factory.CreateClient());
    }

    private static object LoadOneNote => new { items = new[] { new { slot = 1, count = 1 } } };

    private async Task<Guid> AtmInMaintenanceAsync()
    {
        var atmId = await _api.CreateInServiceAtmAsync(try100: 10);
        await _api.PostAsync($"/api/atms/{atmId}/maintenance", null, HttpStatusCode.OK, ApiClient.User("ops"));
        return atmId;
    }

    [Fact]
    public async Task Loading_cash_without_X_User_is_rejected()
    {
        var atmId = await AtmInMaintenanceAsync();

        var response = await _api.PostAsync($"/api/atms/{atmId}/cash-loads", LoadOneNote, HttpStatusCode.BadRequest);

        response.ErrorCode().Should().Be("USER_HEADER_REQUIRED");
    }

    [Fact]
    public async Task Loading_cash_with_a_malformed_X_User_is_rejected()
    {
        var atmId = await AtmInMaintenanceAsync();

        var response = await _api.PostAsync($"/api/atms/{atmId}/cash-loads", LoadOneNote, HttpStatusCode.BadRequest,
            ApiClient.User("Ensar Gokhan"));

        response.ErrorCode().Should().Be("INVALID_USER");
    }

    [Fact]
    public async Task The_user_who_loaded_the_cash_is_recorded_in_the_ledger()
    {
        var atmId = await AtmInMaintenanceAsync();

        await _api.PostAsync($"/api/atms/{atmId}/cash-loads", LoadOneNote, HttpStatusCode.OK, ApiClient.User("ensar.gokhan"));

        var loads = await _api.GetAsync($"/api/atms/{atmId}/movements?type=Load");
        loads.AsArray().Select(m => m!["performedBy"]!.GetValue<string>()).Should().Contain("ensar.gokhan");
    }
}
