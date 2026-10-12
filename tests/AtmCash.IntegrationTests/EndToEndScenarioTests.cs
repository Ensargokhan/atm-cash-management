using System.Net;
using AtmCash.IntegrationTests.Infrastructure;

namespace AtmCash.IntegrationTests;

/// <summary>The Postman scenario as an automated test, plus lifecycle and error-model checks.</summary>
[Collection(ApiCollection.Name)]
public sealed class EndToEndScenarioTests
{
    private readonly ApiClient _api;

    public EndToEndScenarioTests(AtmApiFactory factory)
    {
        _api = new ApiClient(factory.CreateClient());
    }

    [Fact]
    public async Task Create_configure_load_serve_withdraw_deposit_maintain_reconcile_report()
    {
        // create ATM
        var atm = await _api.PostAsync("/api/atms", new { code = $"E2E-{Guid.NewGuid():N}"[..16], name = "Kadikoy" }, HttpStatusCode.Created);
        var atmId = atm.Id();
        atm["status"]!.GetValue<string>().Should().Be("Unconfigured");

        // withdrawal before service is rejected with a meaningful error
        (await _api.QuoteAsync(atmId, 100, expected: HttpStatusCode.Conflict)).ErrorCode().Should().Be("OPERATION_NOT_ALLOWED_IN_STATE");

        // configure cassettes
        await _api.SendAsync(HttpMethod.Put, $"/api/atms/{atmId}/cassettes", new
        {
            cassettes = new object[]
            {
                new { slot = 1, type = "Recycling", currency = "TRY", denomination = 100 },
                new { slot = 2, type = "Recycling", currency = "TRY", denomination = 50 },
                new { slot = 3, type = "Recycling", currency = "TRY", denomination = 20 },
                new { slot = 4, type = "Recycling", currency = "EUR", denomination = 50 },
                new { slot = 5, type = "Deposit" }
            }
        }, HttpStatusCode.OK, ApiClient.User("ops"));

        // load cash
        var loaded = await _api.PostAsync($"/api/atms/{atmId}/cash-loads", new
        {
            items = new[] { new { slot = 1, count = 50 }, new { slot = 2, count = 50 }, new { slot = 3, count = 50 }, new { slot = 4, count = 40 } }
        }, HttpStatusCode.OK, ApiClient.User("loader"));
        loaded["status"]!.GetValue<string>().Should().Be("Loaded");

        // put in service
        await _api.PostAsync($"/api/atms/{atmId}/put-in-service", null, HttpStatusCode.OK, ApiClient.User("ops"));

        // quote + confirm
        var quote = await _api.QuoteAsync(atmId, 1_160, expected: HttpStatusCode.Created);
        quote["isExactMatch"]!.GetValue<bool>().Should().BeTrue();
        var paid = await _api.ConfirmAsync(quote.Id("quoteId"), Guid.NewGuid().ToString(), expected: HttpStatusCode.OK);
        paid["dispensedAmount"]!.GetValue<int>().Should().Be(1_160);

        // deposit
        var deposit = await _api.PostAsync($"/api/atms/{atmId}/deposits", new
        {
            notes = new object[]
            {
                new { currency = "TRY", denomination = 100, count = 5 },
                new { currency = "USD", denomination = 20, count = 3 }
            },
            unrecognizedCount = 1
        }, HttpStatusCode.OK);
        deposit["accepted"]!.AsArray().Should().HaveCount(2);
        deposit["returned"]!.AsArray().Should().ContainSingle();

        // switch to maintenance
        await _api.PostAsync($"/api/atms/{atmId}/maintenance", null, HttpStatusCode.OK, ApiClient.User("ops"));

        // reconcile: two TRY 100 notes missing (200 TRY <= 1,000) -> applied immediately
        var current = await _api.GetAsync($"/api/atms/{atmId}");
        int Count(int slot) => current["cassettes"]!.AsArray().Single(c => c!["slot"]!.GetValue<int>() == slot)!["count"]!.GetValue<int>();
        var reconciliation = await _api.PostAsync($"/api/atms/{atmId}/reconciliations", new
        {
            counts = new object[]
            {
                new { slot = 1, count = Count(1) - 2 },
                new { slot = 2, count = Count(2) },
                new { slot = 3, count = Count(3) },
                new { slot = 4, count = Count(4) },
                new { slot = 5, currency = "USD", denomination = 20, count = 3 }
            }
        }, HttpStatusCode.Created, ApiClient.User("alice"));
        reconciliation["status"]!.GetValue<string>().Should().Be("Applied");

        // back in service, report and ledger check
        await _api.PostAsync($"/api/atms/{atmId}/put-in-service", null, HttpStatusCode.OK, ApiClient.User("ops"));
        var report = await _api.GetAsync($"/api/reports/operations?atmId={atmId}");
        var atmReport = report["atms"]!.AsArray().Single()!;
        atmReport["cassettes"]!.AsArray().Should().HaveCount(5);
        atmReport["totals"]!.AsArray().Select(t => t!["currency"]!.GetValue<string>()).Should().BeEquivalentTo("EUR", "TRY", "USD");
        atmReport["totalTry"]!.GetValue<decimal>().Should().BeGreaterThan(0);

        await _api.AssertLedgerConsistentAsync(atmId);
    }

    [Fact]
    public async Task Large_reconciliation_difference_needs_approval_by_a_different_user()
    {
        var atmId = await _api.CreateInServiceAtmAsync(try100: 50, usd100: 10);
        await _api.PostAsync($"/api/atms/{atmId}/maintenance", null, HttpStatusCode.OK, ApiClient.User("ops"));

        var reconciliation = await _api.PostAsync($"/api/atms/{atmId}/reconciliations", new
        {
            counts = new object[]
            {
                new { slot = 1, count = 38 },
                new { slot = 2, count = 0 },
                new { slot = 3, count = 0 },
                new { slot = 4, count = 10 }
            }
        }, HttpStatusCode.Created, ApiClient.User("alice"));
        var id = reconciliation.Id();
        reconciliation["status"]!.GetValue<string>().Should().Be("AwaitingApproval");

        (await _api.PostAsync($"/api/atms/{atmId}/put-in-service", null, HttpStatusCode.Conflict, ApiClient.User("ops")))
            .ErrorCode().Should().Be("RECONCILIATION_AWAITING_APPROVAL");
        (await _api.PostAsync($"/api/reconciliations/{id}/approve", null, HttpStatusCode.UnprocessableEntity, ApiClient.User("alice")))
            .ErrorCode().Should().Be("FOUR_EYES_VIOLATION");
        (await _api.PostAsync($"/api/reconciliations/{id}/approve", null, HttpStatusCode.BadRequest))
            .ErrorCode().Should().Be("USER_HEADER_REQUIRED");

        var approved = await _api.PostAsync($"/api/reconciliations/{id}/approve", null, HttpStatusCode.OK, ApiClient.User("bob"));
        approved["status"]!.GetValue<string>().Should().Be("Approved");

        await _api.PostAsync($"/api/atms/{atmId}/put-in-service", null, HttpStatusCode.OK, ApiClient.User("ops"));
        (await _api.CassetteAsync(atmId, 1))["count"]!.GetValue<int>().Should().Be(38);
        await _api.AssertLedgerConsistentAsync(atmId);
    }

    [Fact]
    public async Task Errors_use_problem_details_with_error_codes()
    {
        var notFound = await _api.GetAsync($"/api/atms/{Guid.NewGuid()}", HttpStatusCode.NotFound);
        notFound.ErrorCode().Should().Be("ATM_NOT_FOUND");
        notFound["title"].Should().NotBeNull();

        var atm = await _api.PostAsync("/api/atms", new { code = $"ERR-{Guid.NewGuid():N}"[..16] }, HttpStatusCode.Created);
        var invalid = await _api.SendAsync(HttpMethod.Put, $"/api/atms/{atm.Id()}/cassettes", new
        {
            cassettes = new object[]
            {
                new { slot = 1, type = "Recycling", currency = "TRY", denomination = 100 },
                new { slot = 2, type = "Recycling", currency = "TRY", denomination = 50 },
                new { slot = 3, type = "Recycling", currency = "TRY", denomination = 20 },
                new { slot = 4, type = "Recycling", currency = "TRY", denomination = 500 },
                new { slot = 5, type = "Deposit" }
            }
        }, HttpStatusCode.UnprocessableEntity, ApiClient.User("ops"));
        invalid.ErrorCode().Should().Be("INVALID_CASSETTE_CONFIGURATION");

        var validation = await _api.PostAsync("/api/atms", new { code = "" }, HttpStatusCode.BadRequest);
        validation.ErrorCode().Should().Be("VALIDATION_FAILED");

        var transition = await _api.PostAsync($"/api/atms/{atm.Id()}/maintenance", null, HttpStatusCode.Conflict, ApiClient.User("ops"));
        transition.ErrorCode().Should().Be("INVALID_STATE_TRANSITION");
    }
}
