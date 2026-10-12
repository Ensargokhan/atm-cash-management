using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AtmCash.IntegrationTests.Infrastructure;

/// <summary>Thin HTTP helper for the scenarios used by the tests.</summary>
public sealed class ApiClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static int _cardSequence;

    public ApiClient(HttpClient http)
    {
        Http = http;
    }

    public HttpClient Http { get; }

    /// <summary>A card number no other test uses (keeps daily limits independent).</summary>
    public static string NewCardNumber() =>
        $"4{Interlocked.Increment(ref _cardSequence):D6}{Random.Shared.NextInt64(100_000_000, 999_999_999)}";

    public async Task<JsonNode> SendAsync(HttpMethod method, string url, object? body = null, HttpStatusCode? expected = null,
        IDictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            request.Headers.Add(name, value);
        }

        using var response = await Http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (expected is not null && response.StatusCode != expected)
        {
            throw new Xunit.Sdk.XunitException($"{method} {url}: expected {(int)expected} but got {(int)response.StatusCode}: {text}");
        }

        var node = string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text)!;
        if (node is JsonObject obj)
        {
            obj["__status"] = (int)response.StatusCode;
        }

        return node;
    }

    public Task<JsonNode> PostAsync(string url, object? body = null, HttpStatusCode? expected = null, IDictionary<string, string>? headers = null) =>
        SendAsync(HttpMethod.Post, url, body, expected, headers);

    public Task<JsonNode> GetAsync(string url, HttpStatusCode? expected = HttpStatusCode.OK) =>
        SendAsync(HttpMethod.Get, url, null, expected);

    /// <summary>Creates an ATM with the standard cassette layout (1: TRY 100, 2: TRY 50, 3: TRY 20, 4: USD 100, 5: deposit), loads it and puts it in service.</summary>
    public async Task<Guid> CreateInServiceAtmAsync(int try100 = 0, int try50 = 0, int try20 = 0, int usd100 = 10,
        (int Slot, string Currency, int Denomination)[]? layout = null)
    {
        var atm = await PostAsync("/api/atms", new { code = $"ATM-{Guid.NewGuid():N}"[..20], name = "Test ATM" }, HttpStatusCode.Created);
        var atmId = atm["id"]!.GetValue<Guid>();

        layout ??= new[] { (1, "TRY", 100), (2, "TRY", 50), (3, "TRY", 20), (4, "USD", 100) };
        var cassettes = layout
            .Select(c => (object)new { slot = c.Slot, type = "Recycling", currency = c.Currency, denomination = c.Denomination })
            .Append(new { slot = 5, type = "Deposit" })
            .ToArray();
        await SendAsync(HttpMethod.Put, $"/api/atms/{atmId}/cassettes", new { cassettes }, HttpStatusCode.OK, User("ops"));

        var items = new[] { (1, try100), (2, try50), (3, try20), (4, usd100) }
            .Where(i => i.Item2 > 0)
            .Select(i => new { slot = i.Item1, count = i.Item2 })
            .ToArray();
        await PostAsync($"/api/atms/{atmId}/cash-loads", new { items }, HttpStatusCode.OK, User("loader"));
        await PostAsync($"/api/atms/{atmId}/put-in-service", null, HttpStatusCode.OK, User("ops"));
        return atmId;
    }

    public Task<JsonNode> QuoteAsync(Guid atmId, int amount, string? card = null, string currency = "TRY", HttpStatusCode? expected = null) =>
        PostAsync($"/api/atms/{atmId}/withdrawal-quotes", new { cardNumber = card ?? NewCardNumber(), currency, amount }, expected);

    public Task<JsonNode> ConfirmAsync(Guid quoteId, string? key, string? option = null, HttpStatusCode? expected = null) =>
        PostAsync($"/api/withdrawal-quotes/{quoteId}/confirm", option is null ? null : new { option }, expected,
            key is null ? null : new Dictionary<string, string> { ["Idempotency-Key"] = key });

    public static Dictionary<string, string> User(string name) => new() { ["X-User"] = name };

    public async Task<JsonNode> CassetteAsync(Guid atmId, int slot)
    {
        var atm = await GetAsync($"/api/atms/{atmId}");
        return atm["cassettes"]!.AsArray().Single(c => c!["slot"]!.GetValue<int>() == slot)!;
    }

    public async Task AssertLedgerConsistentAsync(Guid atmId)
    {
        var report = await GetAsync($"/api/atms/{atmId}/ledger/consistency");
        if (!report["isConsistent"]!.GetValue<bool>())
        {
            throw new Xunit.Sdk.XunitException($"Ledger is inconsistent: {report["differences"]}");
        }
    }
}

public static class JsonNodeExtensions
{
    public static int Status(this JsonNode node) => node["__status"]!.GetValue<int>();

    public static string? ErrorCode(this JsonNode node) => node["errorCode"]?.GetValue<string>();

    public static Guid Id(this JsonNode node, string property = "id") => node[property]!.GetValue<Guid>();
}
