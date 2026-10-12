using System.Net;
using AtmCash.IntegrationTests.Infrastructure;

namespace AtmCash.IntegrationTests;

/// <summary>ATM status changes and exchange rate updates are kept as history with the X-User who made them.</summary>
[Collection(ApiCollection.Name)]
public sealed class AuditHistoryTests
{
    private readonly ApiClient _api;

    public AuditHistoryTests(AtmApiFactory factory)
    {
        _api = new ApiClient(factory.CreateClient());
    }

    [Fact]
    public async Task Status_history_shows_every_transition_and_who_made_it()
    {
        var atmId = await _api.CreateInServiceAtmAsync(try100: 10);
        await _api.PostAsync($"/api/atms/{atmId}/maintenance", null, HttpStatusCode.OK, ApiClient.User("night.shift"));

        var history = await _api.GetAsync($"/api/atms/{atmId}/status-history");

        // The test clock does not move between requests, so the order is not asserted here (see the domain tests).
        history.AsArray()
            .Select(c => $"{c!["fromStatus"]!.GetValue<string>()}->{c["toStatus"]!.GetValue<string>()} by {c["performedBy"]!.GetValue<string>()}")
            .Should().BeEquivalentTo(
                "Unconfigured->Configured by ops",
                "Configured->Loaded by loader",
                "Loaded->InService by ops",
                "InService->Maintenance by night.shift");
    }

    [Fact]
    public async Task Status_change_without_X_User_is_rejected_and_not_recorded()
    {
        var atmId = await _api.CreateInServiceAtmAsync(try100: 10);

        (await _api.PostAsync($"/api/atms/{atmId}/maintenance", null, HttpStatusCode.BadRequest))
            .ErrorCode().Should().Be("USER_HEADER_REQUIRED");

        var history = await _api.GetAsync($"/api/atms/{atmId}/status-history");
        history.AsArray().Should().HaveCount(3);
        (await _api.GetAsync($"/api/atms/{atmId}"))["status"]!.GetValue<string>().Should().Be("InService");
    }

    [Fact]
    public async Task Status_history_of_an_unknown_atm_is_not_found()
    {
        (await _api.GetAsync($"/api/atms/{Guid.NewGuid()}/status-history", HttpStatusCode.NotFound))
            .ErrorCode().Should().Be("ATM_NOT_FOUND");
    }

    [Fact]
    public async Task Exchange_rate_update_is_kept_in_the_rate_history()
    {
        var rates = await _api.GetAsync("/api/exchange-rates");
        var original = rates.AsArray().Single(r => r!["currency"]!.GetValue<string>() == "EUR")!["rateToTry"]!.GetValue<decimal>();
        var updated = original + 1.25m;

        await _api.SendAsync(HttpMethod.Put, "/api/exchange-rates/EUR", new { rateToTry = updated }, HttpStatusCode.OK,
            ApiClient.User("treasury"));
        // Restore the rate so other tests keep valuing EUR as before.
        await _api.SendAsync(HttpMethod.Put, "/api/exchange-rates/EUR", new { rateToTry = original }, HttpStatusCode.OK,
            ApiClient.User("treasury"));

        var history = await _api.GetAsync("/api/exchange-rates/history?currency=EUR");

        var entries = history.AsArray().Select(c => (
            Old: c!["oldRateToTry"]!.GetValue<decimal>(),
            New: c["newRateToTry"]!.GetValue<decimal>(),
            By: c["changedBy"]!.GetValue<string>())).ToList();
        entries.Should().Contain((original, updated, "treasury"));
        entries.Should().Contain((updated, original, "treasury"));
        history.AsArray().Should().OnlyContain(c => c!["currency"]!.GetValue<string>() == "EUR");
    }

    [Fact]
    public async Task Exchange_rate_update_without_X_User_is_rejected()
    {
        var response = await _api.SendAsync(HttpMethod.Put, "/api/exchange-rates/USD", new { rateToTry = 1m }, HttpStatusCode.BadRequest);

        response.ErrorCode().Should().Be("USER_HEADER_REQUIRED");
    }
}
