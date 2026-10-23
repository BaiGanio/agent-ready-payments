using Microsoft.Extensions.AI;

namespace Payments.Agent.Tests;

/// <summary>One request the fake model was sent, kept so a test can read it back.</summary>
public sealed record RecordedChatRequest(
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<string> ToolsOffered);

/// <summary>
/// The fake model. The test writes the script, so the "model" says exactly the same thing on
/// every run.
/// <para>
/// A real model in the suite would be slow and would change its mind, and a test that fails
/// one time in ten teaches nothing. Judging a real model is the evaluation set's job
/// (steps 21–23), not the suite's.
/// </para>
/// </summary>
public sealed class ScriptedChatClient : IChatClient
{
    public const string FakeModelId = "fake-model";

    private readonly Queue<Func<CancellationToken, Task<ChatResponse>>> _replies = new();

    public List<RecordedChatRequest> Requests { get; } = [];

    /// <summary>
    /// Runs just before a reply is handed back, with the number of this request, starting at 1.
    /// A test that has to prove something happened <b>before</b> the next model call looks here:
    /// from inside the loop there is no other moment to look at.
    /// </summary>
    public Action<int>? OnRequest { get; set; }

    /// <summary>The model asks for a tool.</summary>
    public ScriptedChatClient Calls(string tool, IDictionary<string, object?> args)
        => Reply(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent(NextCallId(), tool, args)]));

    /// <summary>The model answers in words and asks for nothing.</summary>
    public ScriptedChatClient Says(string text)
        => Reply(new ChatMessage(ChatRole.Assistant, text));

    /// <summary>The model answers nothing at all: no text, no tool call.</summary>
    public ScriptedChatClient Empty()
        => Reply(new ChatMessage(ChatRole.Assistant, []));

    /// <summary>
    /// The model answers with something that must never reach a user, such as a card number
    /// it copied out of a tool result.
    /// </summary>
    public ScriptedChatClient Leaks(string text)
        => Reply(new ChatMessage(ChatRole.Assistant, text));

    /// <summary>
    /// A tool call whose arguments did not parse. <c>Arguments</c> is null and
    /// <c>Exception</c> is set: that is exactly what
    /// <c>FunctionCallContent.CreateFromParsedArguments</c> builds from broken JSON
    /// (measured 2026-10-22).
    /// </summary>
    public ScriptedChatClient BrokenArgs(string tool)
    {
        var call = new FunctionCallContent(NextCallId(), tool, arguments: null)
        {
            Exception = new InvalidOperationException("the arguments were not valid JSON"),
        };

        return Reply(new ChatMessage(ChatRole.Assistant, [call]));
    }

    /// <summary>The model call itself fails, the way a dead <c>llama-server</c> fails.</summary>
    public ScriptedChatClient Throws()
    {
        _replies.Enqueue(_ => throw new HttpRequestException("the model is not answering"));

        return this;
    }

    /// <summary>
    /// The model takes this long to answer. For the turn deadline and for cancel.
    /// <para>
    /// The wait honours the token the loop passed in. A wait that ignored it would keep the
    /// test sitting here for the whole delay after the deadline fired, so the test would be
    /// slow and would prove nothing about when the loop gave up.
    /// </para>
    /// </summary>
    public ScriptedChatClient Slow(TimeSpan delay, string text = "sorry, that took a while")
    {
        _replies.Enqueue(async ct =>
        {
            await Task.Delay(delay, ct);

            return Response(new ChatMessage(ChatRole.Assistant, text));
        });

        return this;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(new RecordedChatRequest(
            [.. messages],
            [.. options?.Tools?.Select(t => t.Name) ?? []]));

        if (_replies.Count == 0)
        {
            // Never answer "" to a dry queue. A loop fed an empty answer limps on and the test
            // fails somewhere else, pointing at the wrong thing.
            throw new InvalidOperationException(
                $"ScriptedChatClient ran out of replies on request {Requests.Count}. " +
                "The loop asked the model more times than the test scripted.");
        }

        OnRequest?.Invoke(Requests.Count);

        return await _replies.Dequeue()(cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The loop does not stream.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    private ScriptedChatClient Reply(ChatMessage message)
    {
        _replies.Enqueue(_ => Task.FromResult(Response(message)));

        return this;
    }

    /// <summary>Every reply carries a model id, because every step has to record which model spoke.</summary>
    private static ChatResponse Response(ChatMessage message) =>
        new(message) { ModelId = FakeModelId };

    private string NextCallId() => $"call-{_replies.Count + 1}";
}
