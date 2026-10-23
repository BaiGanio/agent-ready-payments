using System.Text.Json;
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
/// </summary>
public sealed class McpToolHost : IToolHost
{
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
        var result = await _client.CallToolAsync(
            name,
            new Dictionary<string, object?>(args),
            cancellationToken: ct);

        // Every tool in Payments.Mcp answers with exactly one text block holding JSON. Single()
        // on purpose: a second block would mean the server changed shape, and guessing which
        // block to read would hide that.
        var json = result.Content.OfType<TextContentBlock>().Single().Text;

        return new ToolOutcome(result.IsError ?? false, json, ProposalIn(json, args));
    }

    public Task<ToolOutcome> ConfirmAsync(PendingRefund refund, CancellationToken ct) =>
        throw new NotImplementedException();

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
