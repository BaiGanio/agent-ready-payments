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
/// An answer the stub holds open until the test lets it go. The test needs this to
/// have two confirms in flight at once: without it, the first refund returns before
/// the second one starts, and the race the tool must survive never happens.
/// </summary>
public sealed class GatedAnswer
{
    private readonly TaskCompletionSource _arrived =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _released =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the request reaches the stub. Await it, never a sleep.</summary>
    public Task Arrived => _arrived.Task;

    internal Task Released => _released.Task;

    internal void MarkArrived() => _arrived.TrySetResult();

    /// <summary>Lets the held request answer at last.</summary>
    public void Release() => _released.TrySetResult();
}

/// <summary>
/// A fake HTTP back end. It records every request and answers from a queue
/// the test fills up front. An empty queue is a test bug, so it throws.
/// </summary>
public sealed class StubApi : HttpMessageHandler
{
    private readonly Queue<Func<Task<HttpResponseMessage>>> _answers = new();

    /// <summary>
    /// Two confirms at once means two threads in here. The queue and the list are
    /// plain, so the record and the dequeue happen together under this lock.
    /// </summary>
    private readonly object _gate = new();

    public List<RecordedRequest> Requests { get; } = new();

    /// <summary>Queue one answer: a status code and an optional JSON body.</summary>
    public void Enqueue(HttpStatusCode status, object? body = null)
        => Enqueue(() => Task.FromResult(Response(status, body)));

    /// <summary>Queue one failure: the next call throws instead of answering.</summary>
    public void EnqueueThrow(Exception error)
        => Enqueue(() => throw error);

    /// <summary>
    /// Queue one answer the stub holds open. Await the returned handle's
    /// <see cref="GatedAnswer.Arrived"/> to know the call is at the API, then
    /// <see cref="GatedAnswer.Release"/> to let it finish.
    /// </summary>
    public GatedAnswer EnqueueGated(HttpStatusCode status, object? body = null)
    {
        var gate = new GatedAnswer();

        Enqueue(async () =>
        {
            gate.MarkArrived();
            await gate.Released;

            return Response(status, body);
        });

        return gate;
    }

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

        Func<Task<HttpResponseMessage>> answer;

        lock (_gate)
        {
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

            answer = _answers.Dequeue();
        }

        // Outside the lock: a gated answer waits here, and a second request must
        // still be able to get in and be recorded.
        return await answer();
    }

    private void Enqueue(Func<Task<HttpResponseMessage>> answer)
    {
        lock (_gate) _answers.Enqueue(answer);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, object? body)
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
    }
}
