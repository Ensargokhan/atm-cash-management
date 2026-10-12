using System.Net;
using AtmCash.Infrastructure.Outbox;
using AtmCash.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AtmCash.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class WithdrawalTests
{
    private readonly AtmApiFactory _factory;
    private readonly ApiClient _api;

    public WithdrawalTests(AtmApiFactory factory)
    {
        _factory = factory;
        _api = new ApiClient(factory.CreateClient());
    }

    [Fact]
    public async Task Expired_quote_cannot_be_confirmed_and_its_notes_become_available_again()
    {
        var atmId = await _api.CreateInServiceAtmAsync(try100: 3);
        var quote = await _api.QuoteAsync(atmId, 300, expected: HttpStatusCode.Created);

        _factory.Clock.Advance(TimeSpan.FromSeconds(61));

        var confirm = await _api.ConfirmAsync(quote.Id("quoteId"), Guid.NewGuid().ToString(), expected: HttpStatusCode.Conflict);
        confirm.ErrorCode().Should().Be("QUOTE_EXPIRED");

        // The next quote on the ATM releases the expired reservation first.
        await _api.QuoteAsync(atmId, 300, expected: HttpStatusCode.Created);
        var expired = await _api.GetAsync($"/api/withdrawal-quotes/{quote.Id("quoteId")}");
        expired["status"]!.GetValue<string>().Should().Be("Expired");
        await _api.AssertLedgerConsistentAsync(atmId);
    }

    [Fact]
    public async Task Cancelled_quote_releases_notes_and_cannot_be_confirmed()
    {
        var atmId = await _api.CreateInServiceAtmAsync(try100: 3);
        var quote = await _api.QuoteAsync(atmId, 300, expected: HttpStatusCode.Created);
        var quoteId = quote.Id("quoteId");

        await _api.PostAsync($"/api/withdrawal-quotes/{quoteId}/cancel", null, HttpStatusCode.OK);
        var confirm = await _api.ConfirmAsync(quoteId, Guid.NewGuid().ToString(), expected: HttpStatusCode.Conflict);

        confirm.ErrorCode().Should().Be("QUOTE_NOT_PENDING");
        (await _api.CassetteAsync(atmId, 1))["availableCount"]!.GetValue<int>().Should().Be(3);
    }

    [Fact]
    public async Task Confirm_requires_an_idempotency_key_and_rejects_reusing_it_for_another_request()
    {
        var atmId = await _api.CreateInServiceAtmAsync(try100: 5, try20: 10);
        var quote = await _api.QuoteAsync(atmId, 130, expected: HttpStatusCode.Created);
        var quoteId = quote.Id("quoteId");

        (await _api.ConfirmAsync(quoteId, key: null, "Lower", HttpStatusCode.BadRequest)).ErrorCode().Should().Be("IDEMPOTENCY_KEY_REQUIRED");
        (await _api.ConfirmAsync(quoteId, "k1", expected: HttpStatusCode.UnprocessableEntity)).ErrorCode().Should().Be("QUOTE_OPTION_NOT_AVAILABLE");

        var paid = await _api.ConfirmAsync(quoteId, "k2", "Upper", HttpStatusCode.OK);
        paid["dispensedAmount"]!.GetValue<int>().Should().Be(140);

        (await _api.ConfirmAsync(quoteId, "k2", "Lower", HttpStatusCode.UnprocessableEntity)).ErrorCode().Should().Be("IDEMPOTENCY_KEY_REUSED");
    }

    [Fact]
    public async Task Reserved_but_unconfirmed_amounts_count_towards_the_daily_limit()
    {
        var card = ApiClient.NewCardNumber();
        var atmId = await _api.CreateInServiceAtmAsync(try100: 100, usd100: 20);

        var first = await _api.QuoteAsync(atmId, 9_000, card, expected: HttpStatusCode.Created);
        first["remainingDailyLimit"]!.GetValue<int>().Should().Be(11_000);

        var tooMuch = await _api.QuoteAsync(atmId, 1_100 + 10_000, card, expected: HttpStatusCode.UnprocessableEntity);
        tooMuch.ErrorCode().Should().Be("DAILY_LIMIT_EXCEEDED");

        // Currencies are tracked separately.
        await _api.QuoteAsync(atmId, 1_000, card, "USD", HttpStatusCode.Created);

        await _api.PostAsync($"/api/withdrawal-quotes/{first.Id("quoteId")}/cancel", null, HttpStatusCode.OK);
        await _api.QuoteAsync(atmId, 11_000, card, expected: HttpStatusCode.Created);
    }

    [Fact]
    public async Task Low_cash_alert_is_written_to_the_outbox_and_published()
    {
        var atmId = await _api.CreateInServiceAtmAsync(try100: 12);

        await _api.QuoteAsync(atmId, 300, expected: HttpStatusCode.Created);

        var message = await _factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Type == "LowCashAlertRaised" && m.Payload.Contains(atmId.ToString())));
        message.Payload.Should().Contain("\"availableCount\":9");

        // The hosted service logs it and marks it as sent.
        OutboxMessage? sent = null;
        for (var i = 0; i < 50 && sent?.SentAt is null; i++)
        {
            await Task.Delay(200);
            sent = await _factory.WithDbContextAsync(db => db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id));
        }

        sent!.SentAt.Should().NotBeNull();
    }


    [Fact]
    public async Task Replayed_confirm_is_marked_with_the_Idempotent_Replayed_header()
    {
        var atmId = await _api.CreateInServiceAtmAsync(try100: 5);
        var quote = await _api.QuoteAsync(atmId, 100, expected: HttpStatusCode.Created);
        var key = Guid.NewGuid().ToString();

        using var first = await SendConfirmAsync(quote.Id("quoteId"), key);
        using var replay = await SendConfirmAsync(quote.Id("quoteId"), key);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Headers.Contains("Idempotent-Replayed").Should().BeFalse("the first confirm really executes");

        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.Headers.GetValues("Idempotent-Replayed").Should().ContainSingle().Which.Should().Be("true");
    }

    private async Task<HttpResponseMessage> SendConfirmAsync(Guid quoteId, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/withdrawal-quotes/{quoteId}/confirm");
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await _api.Http.SendAsync(request);
    }

}
