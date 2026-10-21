using System.Net;
using System.Text;
using System.Text.Json;

namespace Payments.Mcp.Tests;

/// <summary>One request the stub saw, kept as plain strings so a test can read it.</summary>
public sealed record RecordedRequest(
    HttpMethod Method,
    string PathAndQuery,
    IReadOnlyDictionary<string, string> Headers,
    string Body);

/// <summary>
/// A fake HTTP back end. It records every request and answers from a queue
/// the test fills up front. An empty queue is a test bug, so it throws.
/// </summary>
public sealed class StubApi : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _answers = new();

    public List<RecordedRequest> Requests { get; } = new();

    /// <summary>Queue one answer: a status code and an optional JSON body.</summary>
    public void Enqueue(HttpStatusCode status, object? body = null)
        => _answers.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status);
            if (body is not null)
            {
                response.Content = new StringContent(
                    body as string ?? JsonSerializer.Serialize(body),
                    Encoding.UTF8,
                    "application/json");
            }
            return response;
        });

    /// <summary>Queue one failure: the next call throws instead of answering.</summary>
    public void EnqueueThrow(Exception error)
        => _answers.Enqueue(() => throw error);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in request.Headers)
        {
            headers[name] = string.Join(",", values);
        }

        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri?.PathAndQuery ?? string.Empty,
            headers,
            body));

        if (_answers.Count == 0)
        {
            throw new InvalidOperationException(
                $"StubApi has no queued answer for {request.Method} {request.RequestUri}. " +
                "Enqueue one in the test, or the tool should not have called the API at all.");
        }

        return _answers.Dequeue()();
    }
}