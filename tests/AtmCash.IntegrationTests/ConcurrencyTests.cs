using System.Net;
using AtmCash.IntegrationTests.Infrastructure;

namespace AtmCash.IntegrationTests;

/// <summary>B6: concurrent requests against a real SQL Server.</summary>
[Collection(ApiCollection.Name)]
public sealed class ConcurrencyTests
{
    private readonly AtmApiFactory _factory;
    private readonly ApiClient _api;

    public ConcurrencyTests(AtmApiFactory factory)
    {
        _factory = factory;
        _api = new ApiClient(factory.CreateClient());
    }

    [Fact]
    public async Task Parallel_quotes_on_the_same_atm_never_reserve_the_same_note_twice()
    {
        // 10 notes of TRY 100. 20 customers ask for 300 TRY at the same moment.
        var atmId = await _api.CreateInServiceAtmAsync(try100: 10);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => _api.QuoteAsync(atmId, 300)));

        var accepted = responses.Where(r => r.Status() == (int)HttpStatusCode.Created).ToList();
        var rejected = responses.Where(r => r.Status() != (int)HttpStatusCode.Created).ToList();

        var reservedByQuotes = accepted
            .SelectMany(q => q["reservations"]!.AsArray())
            .Sum(r => r!["count"]!.GetValue<int>());

        // Three exact 300s (9 notes) and one 100 lower alternative (the last note); everyone else finds no cash.
        reservedByQuotes.Should().Be(10, "every physical note is reserved exactly once");
        accepted.Should().HaveCount(4);
        rejected.Should().OnlyContain(r => r.ErrorCode() == "INSUFFICIENT_CASH");

        var cassette = await _api.CassetteAsync(atmId, 1);
        cassette["count"]!.GetValue<int>().Should().Be(10);
        cassette["reservedCount"]!.GetValue<int>().Should().Be(10);
        cassette["availableCount"]!.GetValue<int>().Should().Be(0);
        await _api.AssertLedgerConsistentAsync(atmId);
    }

    [Fact]
    public async Task Concurrent_confirms_with_the_same_idempotency_key_debit_the_cash_once()
    {
        var atmId = await _api.CreateInServiceAtmAsync(try100: 20);
        var quote = await _api.QuoteAsync(atmId, 500, expected: HttpStatusCode.Created);
        var quoteId = quote.Id("quoteId");
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _api.ConfirmAsync(quoteId, key)));

        responses.Should().OnlyContain(r => r.Status() == (int)HttpStatusCode.OK);
        responses.Select(r => r["confirmedAt"]!.ToString()).Distinct().Should().ContainSingle("every retry returns the stored result");

        var cassette = await _api.CassetteAsync(atmId, 1);
        cassette["count"]!.GetValue<int>().Should().Be(15, "5 notes dispensed once, not 8 times");
        cassette["reservedCount"]!.GetValue<int>().Should().Be(0);

        var withdrawals = await _api.GetAsync($"/api/atms/{atmId}/movements?type=Withdrawal");
        withdrawals.AsArray().Should().ContainSingle();
        await _api.AssertLedgerConsistentAsync(atmId);
    }

    [Fact]
    public async Task Concurrent_quotes_for_one_card_on_different_atms_respect_the_daily_limit()
    {
        var card = ApiClient.NewCardNumber();
        var atms = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => _api.CreateInServiceAtmAsync(try100: 100)));

        // 3 x 8,000 = 24,000 > 20,000: at most two may succeed.
        var responses = await Task.WhenAll(atms.Select(atmId => _api.QuoteAsync(atmId, 8_000, card)));

        responses.Count(r => r.Status() == (int)HttpStatusCode.Created).Should().Be(2);
        responses.Should().ContainSingle(r => r.ErrorCode() == "DAILY_LIMIT_EXCEEDED");
    }
}
