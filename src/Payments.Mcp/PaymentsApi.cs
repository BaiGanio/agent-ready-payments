using System.Text;
using System.Text.Json;

namespace Payments.Mcp;

/// <summary>
/// The only thing in this server that talks to the payments API. A typed
/// HttpClient, so the base address and the X-Api-Key header are set once in
/// setup and no tool can forget them or send the wrong one.
/// </summary>
internal sealed class PaymentsApi(HttpClient http)
{
    internal Task<ApiResult<JsonElement>> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Get, $"payments/{id:D}"), cancellationToken);

    internal Task<ApiResult<JsonElement>> ListAsync(
        string? status, int limit, CancellationToken cancellationToken = default)
    {
        var query = new StringBuilder("payments?limit=").Append(limit);

        if (status is not null)
        {
            query.Append("&status=").Append(Uri.EscapeDataString(status));
        }

        return SendAsync(new HttpRequestMessage(HttpMethod.Get, query.ToString()), cancellationToken);
    }

    internal Task<ApiResult<JsonElement>> RefundAsync(
        Guid id,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"payments/{id:D}/refund")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    // A JSON number, not a string: the API's AmountRequest takes a
                    // decimal, and System.Text.Json does not read a number from a
                    // string. A decimal keeps its scale, so 80.00m writes as 80.00.
                    amount,
                    currency,
                }),
                Encoding.UTF8,
                "application/json"),
        };

        request.Headers.Add("Idempotency-Key", idempotencyKey);

        return SendAsync(request, cancellationToken);
    }

    private async Task<ApiResult<JsonElement>> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using (request)
            {
                using var response = await http.SendAsync(request, cancellationToken);
                var text = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var (code, detail) = ReadProblem(text);

                    return ApiResult<JsonElement>.Problem((int)response.StatusCode, code, detail);
                }

                using var document = JsonDocument.Parse(text);

                return ApiResult<JsonElement>.Success(document.RootElement.Clone());
            }
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Status 0: the call never got an answer. The tool turns this into
            // tool_failed, and a refund turns it into refund_in_doubt.
            return ApiResult<JsonElement>.Problem(0, null, error.Message);
        }
    }

    /// <summary>
    /// Reads the problem's "code" and "detail" the same way ApiFixture.ErrorCodeAsync does.
    /// A body that is not ProblemDetails is not an error here: the status still decides.
    /// </summary>
    private static (string? Code, string? Detail) ReadProblem(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return (null, null);
            }

            var code = document.RootElement.TryGetProperty("code", out var found)
                ? found.GetString()
                : null;

            var detail = document.RootElement.TryGetProperty("detail", out var text)
                ? text.GetString()
                : null;

            return (code, detail);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}