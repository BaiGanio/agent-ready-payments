using System.Net;
using Microsoft.Extensions.AI;
using Xunit;

namespace Payments.Agent.Tests;

/// <summary>
/// The six rules the loop has to carry. No trait: these run in CI, because nothing here needs
/// a database, a model or a network.
/// <para>
/// The model is scripted and the tools are the real MCP server over pipes. So a failure here
/// is the loop's fault, never a model's mood.
/// </para>
/// </summary>
public sealed class LoopTests
{
    private const string PaymentId = "0199c3f0-1a2b-7c3d-8e4f-5a6b7c8d9e0f";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_question_that_needs_no_tool_is_answered_directly()
    {
        await using var harness = await AgentHarness.StartAsync();

        harness.Model.Says("Payments are settled the next working day.");

        var run = await harness.Loop.StartAsync("when do payments settle?", Ct);

        // One pass, and the loop stopped. A loop that asks again after a plain answer burns
        // the conversation and costs money for nothing.
        Assert.Single(harness.Model.Requests);

        // No tool was asked for, so the API must not have been touched at all.
        Assert.Empty(harness.Api.Requests);

        Assert.Equal(RunStatus.Done, run.Status);
        Assert.Equal("Payments are settled the next working day.", run.Answer);
        Assert.Equal(RunStatus.Done, harness.Store.Saved(run.Id).Run.Status);
    }

    [Fact]
    public async Task The_host_runs_the_tool_the_model_asked_for()
    {
        await using var harness = await AgentHarness.StartAsync();

        harness.Api.Enqueue(HttpStatusCode.OK, StubApi.Payment(PaymentId));

        harness.Model
            .Calls("get_payment", new Dictionary<string, object?> { ["payment_id"] = PaymentId })
            .Says("That payment is captured, 80.00 EUR.");

        await harness.Loop.StartAsync("what is the state of that payment?", Ct);

        // Exactly one request, and the right one. The loop must run the tool the model named,
        // with the arguments the model gave, and nothing else.
        var request = Assert.Single(harness.Api.Requests);

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"/payments/{PaymentId}", request.PathAndQuery);
    }

    [Fact]
    public async Task The_tool_result_goes_back_with_the_same_call_id()
    {
        await using var harness = await AgentHarness.StartAsync();

        harness.Api.Enqueue(HttpStatusCode.OK, StubApi.Payment(PaymentId));

        harness.Model
            .Calls("get_payment", new Dictionary<string, object?> { ["payment_id"] = PaymentId })
            .Says("Captured, 80.00 EUR.");

        await harness.Loop.StartAsync("read that payment", Ct);

        Assert.Equal(2, harness.Model.Requests.Count);

        var asked = harness.Model.Requests[0];
        var answered = harness.Model.Requests[1];

        var call = answered.Messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionCallContent>()
            .Single();

        var result = answered.Messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>()
            .Single();

        // The call id is the thread between the ask and the answer. Send back a different one
        // and the model cannot tell which result belongs to which call, so it calls again.
        Assert.Equal(call.CallId, result.CallId);
        Assert.NotEmpty(asked.ToolsOffered);
    }

    [Fact]
    public async Task The_model_sees_the_whole_history_every_time()
    {
        await using var harness = await AgentHarness.StartAsync();

        harness.Api.Enqueue(HttpStatusCode.OK, StubApi.Payment(PaymentId));
        harness.Api.Enqueue(HttpStatusCode.OK, StubApi.Payment(PaymentId));

        harness.Model
            .Calls("get_payment", new Dictionary<string, object?> { ["payment_id"] = PaymentId })
            .Calls("get_payment", new Dictionary<string, object?> { ["payment_id"] = PaymentId })
            .Says("Still captured.");

        await harness.Loop.StartAsync("check it twice", Ct);

        Assert.Equal(3, harness.Model.Requests.Count);

        // A model has no memory. Each request has to repeat everything that came before, in
        // order, or the model answers from half a conversation.
        for (var n = 1; n < harness.Model.Requests.Count; n++)
        {
            var before = harness.Model.Requests[n - 1].Messages;
            var now = harness.Model.Requests[n].Messages;

            Assert.True(
                now.Count > before.Count,
                $"Request {n + 1} holds {now.Count} messages, request {n} held {before.Count}. "
                + "The history must grow, never shrink.");

            Assert.Equal(
                before.Select(Shape),
                now.Take(before.Count).Select(Shape));
        }
    }

    [Fact]
    public async Task Every_step_is_saved_before_the_next_model_call()
    {
        await using var harness = await AgentHarness.StartAsync();

        harness.Api.Enqueue(HttpStatusCode.OK, StubApi.Payment(PaymentId));

        harness.Model
            .Calls("get_payment", new Dictionary<string, object?> { ["payment_id"] = PaymentId })
            .Says("Captured.");

        AgentStep? stepOne = null;

        // Looking from inside the loop is the only way to prove the order. After the run ends
        // the store holds everything, whichever order it was written in. The loop makes the
        // run id, so the test asks the store for the only run it has.
        harness.Model.OnRequest = n =>
        {
            if (n == 2)
            {
                stepOne = harness.Store.Only()?.Steps.FirstOrDefault();
            }
        };

        var run = await harness.Loop.StartAsync("read it", Ct);

        // A process that dies here must leave the tool call on disk. Otherwise a resume calls
        // the same tool again, and for a refund that is money moved twice.
        Assert.NotNull(stepOne);
        Assert.Equal("get_payment", stepOne.ToolName);
        Assert.False(string.IsNullOrWhiteSpace(stepOne.OutputJson));
        Assert.Equal(run.Id, stepOne.RunId);
    }

    [Fact]
    public async Task Every_step_records_its_provenance()
    {
        var options = new AgentOptions { AgentName = "support-bot", PromptVersion = "v7" };

        await using var harness = await AgentHarness.StartAsync(options);

        harness.Api.Enqueue(HttpStatusCode.OK, StubApi.Payment(PaymentId));

        harness.Model
            .Calls("get_payment", new Dictionary<string, object?> { ["payment_id"] = PaymentId })
            .Says("Captured.");

        var run = await harness.Loop.StartAsync("read it", Ct);

        Assert.Equal("support-bot", run.AgentName);

        var step = Assert.Single(harness.Store.Saved(run.Id).Steps);

        // Six months later somebody asks why this refund happened. Without the model id and
        // the prompt version on the step, the answer is a shrug.
        Assert.Equal(ScriptedChatClient.FakeModelId, step.ModelId);
        Assert.Equal("v7", step.PromptVersion);
        Assert.Contains(PaymentId, step.InputJson);
    }

    /// <summary>
    /// A message as "who said it, and what kind of thing it was". Comparing whole messages
    /// would compare object identity, and the loop is free to rebuild the list each pass.
    /// </summary>
    private static string Shape(ChatMessage message) =>
        $"{message.Role}:{string.Join(",", message.Contents.Select(c => c.GetType().Name))}:{message.Text}";
}
