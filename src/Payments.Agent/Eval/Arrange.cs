using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace Payments.Agent.Eval;

/// <summary>
/// One scenario's own payments, made through the API with the service key, before the agent is
/// started.
/// <para>
/// Through the <b>API</b> and never with an INSERT, for the reason <c>seed-demo</c> gives: a row
/// no endpoint could have produced is not the thing the agent will meet in the demo. Every
/// money-moving POST carries a fresh <c>Idempotency-Key</c>, or the API answers
/// <c>idempotency_key_required</c> and nothing is created.
/// </para>
/// <para>
/// With the <b>service</b> key, not an agent key. Arranging writes and captures, and no agent key
/// may do that: an agent that could create its own payments could create one to refund. This is
/// the only place in the agent process that calls the API directly, and it is not the agent — it
/// is the bench that puts the payment on the table before the agent walks in.
/// </para>
/// <para>
/// Each scenario gets a fresh instance, so <c>{dup}</c> is the payment this scenario just made
/// and never one left behind by yesterday's run. That is what makes "the newest captured
/// payment" a thing a scenario may say.
/// </para>
/// </summary>
public sealed class Arrange(HttpClient client)
{
    /// <summary>M1 Hotel Sofia, the merchant all three lab keys belong to.</summary>
    private const string MerchantId = "11111111-1111-1111-1111-111111111111";

    private readonly Dictionary<string, string> _ids = [];

    /// <summary>Handle to payment id, for the request text and for the final checks.</summary>
    public IReadOnlyDictionary<string, string> Ids => _ids;

    /// <summary>Created, authorised and captured in full. The ordinary payment an agent meets.</summary>
    public Task<string> CapturedAsync(string handle, decimal amount) =>
        MakeAsync(handle, amount, capture: amount);

    /// <summary>Created and authorised, nothing captured. Nothing here is refundable yet.</summary>
    public Task<string> AuthorisedAsync(string handle, decimal amount) =>
        MakeAsync(handle, amount, capture: null);

    /// <summary>
    /// Refunds part of an arranged payment with the service key, so the agent meets a payment
    /// that has a history. The proposal the agent gets must then say what is really left.
    /// </summary>
    public async Task RefundAsync(string handle, decimal amount)
    {
        await PostAsync($"payments/{_ids[handle]}/refund", Money(amount));
    }

    /// <summary>What the payment looks like now. Read after the run, for the final checks.</summary>
    public async Task<PaymentFacts> ReadAsync(string handle)
    {
        var id = _ids[handle];

        using var response = await client.GetAsync($"payments/{id}");

        response.EnsureSuccessStatusCode();

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        return new PaymentFacts(
            id,
            body.GetProperty("status").GetString() ?? "",
            body.GetProperty("capturedAmount").GetDecimal(),
            body.GetProperty("refundedAmount").GetDecimal(),
            body.GetProperty("refundableRemaining").GetDecimal());
    }

    /// <summary>Every handle's facts, re-read. The dictionary <see cref="Outcome"/> carries.</summary>
    public async Task<IReadOnlyDictionary<string, PaymentFacts>> ReadAllAsync()
    {
        var facts = new Dictionary<string, PaymentFacts>();

        foreach (var handle in _ids.Keys)
        {
            facts[handle] = await ReadAsync(handle);
        }

        return facts;
    }

    /// <summary>Puts the real ids into the typed request. <c>{dup}</c> becomes a UUID.</summary>
    public string Fill(string request)
    {
        var text = request;

        foreach (var (handle, id) in _ids)
        {
            text = text.Replace($"{{{handle}}}", id, StringComparison.Ordinal);
        }

        return text;
    }

    private async Task<string> MakeAsync(string handle, decimal amount, decimal? capture)
    {
        using var create = new HttpRequestMessage(HttpMethod.Post, "payments")
        {
            Content = JsonContent.Create(new
            {
                merchantId = MerchantId,
                amount,
                currency = "EUR",
            }),
        };

        create.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        using var created = await client.SendAsync(create);

        var body = await created.Content.ReadAsStringAsync();

        if (!created.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Arranging '{handle}' failed: POST payments answered {(int)created.StatusCode}. {body}");
        }

        var id = JsonDocument.Parse(body).RootElement.GetProperty("id").GetString()!;

        _ids[handle] = id;

        await PostAsync($"payments/{id}/authorise", null);

        if (capture is decimal take)
        {
            await PostAsync($"payments/{id}/capture", Money(take));
        }

        return id;
    }

    /// <summary>
    /// A failed arrange throws. A scenario that scored a model against a payment that was never
    /// captured would report the model's mistake for the bench's mistake, and the number would
    /// be worse than no number.
    /// </summary>
    private async Task PostAsync(string path, object? body)
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
            return;
        }

        // The body is read into a local first: an await inside an interpolated string that
        // string.Create holds is not a thing the compiler will build.
        var refusal = await response.Content.ReadAsStringAsync();

        throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
            $"Arranging failed: POST {path} answered {(int)response.StatusCode}. {refusal}"));
    }

    private static object Money(decimal amount) => new
    {
        amount,
        currency = "EUR",
    };
}
