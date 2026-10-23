using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace Payments.Agent.Commands;

/// <summary>
/// Makes the demo's payments, through the API, with the service key.
/// <para>
/// Through the <b>API</b> and never with an INSERT into the database. A seeded row that no
/// endpoint could have produced is a demo that proves the demo: the double charge has to be two
/// real creates, two real authorises and two real captures, so what the agent reads later is
/// the real thing, with real idempotency keys and a real operation history behind it.
/// </para>
/// <para>
/// Every money-moving POST takes an <c>Idempotency-Key</c>, so every call here sends a fresh
/// one. Without the header the API answers <c>400 idempotency_key_required</c> and nothing is
/// created (<c>CF-042</c> was the same mistake in a doc).
/// </para>
/// </summary>
public static class SeedDemoCommand
{
    /// <summary>
    /// M1 Hotel Sofia. The three lab keys all belong to this merchant, and the API refuses a
    /// create for anybody else's merchant with <c>merchant_mismatch</c>, so a wrong id here
    /// fails loudly rather than seeding the wrong shop.
    /// </summary>
    private const string MerchantId = "11111111-1111-1111-1111-111111111111";

    /// <param name="Amount">The amount to create, authorise and maybe capture.</param>
    /// <param name="Capture">False leaves the payment authorised only.</param>
    /// <param name="Story">What this payment is for, in the demo.</param>
    private sealed record Seed(decimal Amount, bool Capture, string Story);

    private static readonly Seed[] Demo =
    [
        new(240.00m, true, "the hotel charge"),
        new(240.00m, true, "the duplicate charge — the one to refund in the demo"),
        new(100.00m, true, "a small captured payment"),
        new(1200.00m, true, "above support-bot's 500.00 refund ceiling"),
        new(50.00m, false, "authorised only, nothing captured yet"),
    ];

    public static async Task<int> ExecuteAsync(HostSettings settings)
    {
        var devKey = Environment.GetEnvironmentVariable("PAYMENTS_DEV_KEY");

        if (string.IsNullOrWhiteSpace(devKey))
        {
            // The service key, not an agent key: seeding writes and captures, and no agent key
            // is allowed to do that. An agent that could create its own payments could also
            // create one to refund.
            Console.Error.WriteLine(
                "PAYMENTS_DEV_KEY is not set. seed-demo writes payments, which no agent key may do.");
            Console.Error.WriteLine("Fill it in .env, then: set -a; source .env; set +a");

            return 2;
        }

        using var client = new HttpClient { BaseAddress = new Uri(settings.ApiUrl) };

        client.DefaultRequestHeaders.Add("X-Api-Key", devKey);

        Console.WriteLine($"seed-demo  api {settings.ApiUrl}  merchant {MerchantId}");
        Console.WriteLine();

        foreach (var seed in Demo)
        {
            var id = await CreateAsync(client, seed);

            if (id is null)
            {
                return 1;
            }

            if (!await PostAsync(client, $"payments/{id}/authorise", null))
            {
                return 1;
            }

            if (seed.Capture
                && !await PostAsync(client, $"payments/{id}/capture", Amount(seed.Amount)))
            {
                return 1;
            }

            // InvariantCulture, so the amount reads 240.00 and not 240,00. This machine's
            // locale uses a comma, and a comma is exactly the shape the money fence refuses:
            // the demo must not teach the model a separator the tools reject.
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {seed.Amount,8:0.00} EUR  {(seed.Capture ? "Captured  " : "Authorised")}  {id}  {seed.Story}"));
        }

        Console.WriteLine();
        Console.WriteLine("Keep these ids on screen. The two 240.00 payments are the double charge.");
        Console.WriteLine("They are seconds apart, not a minute: the API stamps the time, not this command.");

        return 0;
    }

    private static object Amount(decimal amount) => new
    {
        amount,
        currency = "EUR",
    };

    /// <summary>Creates one payment and returns its id, or null after printing the refusal.</summary>
    private static async Task<string?> CreateAsync(HttpClient client, Seed seed)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "payments")
        {
            Content = JsonContent.Create(new
            {
                merchantId = MerchantId,
                amount = seed.Amount,
                currency = "EUR",
            }),
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        using var response = await client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Complain("POST payments", (int)response.StatusCode, body);

            return null;
        }

        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetString();
    }

    private static async Task<bool> PostAsync(HttpClient client, string path, object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        using var response = await client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        Complain($"POST {path}", (int)response.StatusCode, await response.Content.ReadAsStringAsync());

        return false;
    }

    /// <summary>
    /// A failure body is <c>application/problem+json</c> and always carries a <c>code</c>.
    /// Print that field, because the English changes and the code does not.
    /// </summary>
    private static void Complain(string what, int status, string body)
    {
        var code = "no code";

        try
        {
            if (JsonDocument.Parse(body).RootElement.TryGetProperty("code", out var found))
            {
                code = found.GetString() ?? code;
            }
        }
        catch (JsonException)
        {
            // Not a problem document. The status is still the answer.
        }

        Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{what} answered {status} {code}. Nothing more was seeded."));
        Console.Error.WriteLine(body);
    }
}
