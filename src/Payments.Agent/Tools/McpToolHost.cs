using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Payments.Agent.Tools;

/// <summary>
/// The real tools, over the real MCP protocol.
/// <para>
/// The tool list is read once in <see cref="CreateAsync"/> and kept. Asking the server for its
/// tools on every pass of the loop would put a round trip in front of every model call, and the
/// list does not change inside a turn.
/// </para>
/// <para>
/// This host also holds the key to the refund gate. A proposal comes back from the server with a
/// <c>confirmation_token</c> on it, and this host <b>cuts it out</b> before the result goes into
/// the conversation. The token lives on the run's <see cref="PendingRefund"/>, which only
/// <see cref="ConfirmAsync"/> reads. So the model is never handed the key, and no sentence in any
/// payload can talk it into using one.
/// </para>
/// </summary>
public sealed class McpToolHost : IToolHost
{
    /// <summary>The one tool that moves money, and the only one with two phases.</summary>
    internal const string RefundTool = "refund_payment";

    private McpClient _client = null!;

    public IReadOnlyList<AITool> Tools { get; private set; } = [];

    public static async Task<McpToolHost> CreateAsync(McpClient client, CancellationToken ct = default)
    {
        var host = new McpToolHost { _client = client };

        // An McpClientTool is already an AIFunction, so the model can be offered it as it is.
        var tools = await client.ListToolsAsync(cancellationToken: ct);

        host.Tools = [.. tools];

        return host;
    }

    public async Task<ToolOutcome> CallAsync(
        string name,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken ct)
    {
        var (isError, json) = await SendAsync(name, args, ct);

        var proposal = ProposalIn(json, args);

        // Only a proposal is rewritten. Every other result goes into the conversation exactly
        // as the server wrote it: a host that edits tool output hides what the server said.
        return proposal is null
            ? new ToolOutcome(isError, json, null)
            : new ToolOutcome(isError, WithoutToken(json), proposal);
    }

    /// <summary>
    /// Phase two: the same refund again, with the token a human approved. The three money
    /// arguments come from the saved <see cref="PendingRefund"/>, never from the model's latest
    /// reply, so the refund that runs is the exact one the human read.
    /// </summary>
    public async Task<ToolOutcome> ConfirmAsync(PendingRefund refund, CancellationToken ct)
    {
        var (isError, json) = await SendAsync(
            RefundTool,
            new Dictionary<string, object?>
            {
                ["payment_id"] = refund.PaymentId,
                ["amount"] = refund.Amount,
                ["currency"] = refund.Currency,
                ["confirmation_token"] = refund.Token,
            },
            ct);

        // No proposal can come out of phase two, and nothing here is rewritten: this is the
        // answer about real money, and the model and the audit must see it whole.
        return new ToolOutcome(isError, json, null);
    }

    private async Task<(bool IsError, string Json)> SendAsync(
        string name, IReadOnlyDictionary<string, object?> args, CancellationToken ct)
    {
        var result = await _client.CallToolAsync(
            name,
            new Dictionary<string, object?>(args),
            cancellationToken: ct);

        // Every tool in Payments.Mcp answers with exactly one text block holding JSON. Single()
        // on purpose: a second block would mean the server changed shape, and guessing which
        // block to read would hide that.
        var json = result.Content.OfType<TextContentBlock>().Single().Text;

        return (result.IsError ?? false, json);
    }

    /// <summary>
    /// The proposal as the model may read it: the same answer with the token taken out.
    /// <para>
    /// The server's own <c>next</c> tells the caller to send the token back, and that is true
    /// for a plain MCP client. Here it is not: the model has no token and never will. Leaving
    /// the sentence in would send a weak model off to call the tool with a token it invented,
    /// which the loop then has to refuse. So the host replaces it with what really happens.
    /// </para>
    /// </summary>
    private static string WithoutToken(string json)
    {
        var payload = JsonNode.Parse(json)!.AsObject();

        payload.Remove("confirmation_token");

        payload["next"] =
            "A human is being asked to approve this exact refund. You cannot approve it and " +
            "you have no confirmation_token: this host keeps it and sends it itself, only " +
            "after a human says yes. Do not call refund_payment again for this refund.";

        return payload.ToJsonString();
    }

    /// <summary>
    /// Reads a phase-one refund answer. The token and the words come from the tool, the three
    /// refund arguments from the call the model made: the loop must hold the exact refund a
    /// human is about to see, not a summary of it.
    /// </summary>
    private static PendingRefund? ProposalIn(string json, IReadOnlyDictionary<string, object?> args)
    {
        using var parsed = JsonDocument.Parse(json);
        var body = parsed.RootElement;

        if (!body.TryGetProperty("result", out var result) ||
            result.GetString() != "confirmation_required" ||
            !body.TryGetProperty("confirmation_token", out var token))
        {
            return null;
        }

        return new PendingRefund(
            Text(args, "payment_id"),
            Text(args, "amount"),
            Text(args, "currency"),
            token.GetString() ?? "",
            body.TryGetProperty("proposal", out var words) ? words.GetString() ?? "" : "");
    }

    private static string Text(IReadOnlyDictionary<string, object?> args, string key) =>
        args.TryGetValue(key, out var value) ? value?.ToString() ?? "" : "";
}
