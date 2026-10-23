using System.ClientModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OpenAI;
using Payments.Agent.Storage;
using Payments.Agent.Tools;
using Payments.Migrations;

namespace Payments.Agent.Commands;

/// <summary>
/// Wires the real parts together: llama-server through <c>IChatClient</c>, the MCP server as a
/// child process over stdio, and EF Core on the dev SQL Server. Nothing in <c>AgentLoop</c>
/// knows which of them is real, which is the point: this is the only place that changes when
/// the model, the tools or the store change.
/// <para>
/// It is a class and not a few lines in <c>Program</c> because four of the five commands need a
/// different part of it. <c>show</c> needs the store and nothing else: it must not start a
/// child process or call a model just to print a row.
/// </para>
/// </summary>
public sealed class AgentHost : IAsyncDisposable
{
    private readonly McpClient _mcp;

    private AgentHost(McpClient mcp, AgentLoop loop)
    {
        _mcp = mcp;
        Loop = loop;
    }

    public AgentLoop Loop { get; }

    /// <summary>
    /// The store on its own, for a command that only reads saved runs.
    /// </summary>
    public static IAgentRunStore OpenStore()
    {
        // EfAgentRunStore takes a factory, not a context: one fresh context per call, the way
        // two separate processes would see the row. The two tables come from DbUp, not from EF.
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlServer(LocalDatabases.SqlServer())
            .Options;

        return new EfAgentRunStore(() => new AgentDbContext(options));
    }

    /// <summary>
    /// A loop that is ready to run one agent: its key goes to the MCP child, its options go to
    /// the loop.
    /// </summary>
    public static async Task<AgentHost> CreateAsync(
        HostSettings settings, ResolvedAgent agent, int? crashAfter)
    {
        // The child gets the two variables it needs and nothing else, so one place decides
        // which agent this process is. Note the names differ: our key variable is its
        // PAYMENTS_API_KEY, whichever agent we are.
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "payments-mcp",
            Command = "dotnet",
            Arguments =
            [
                Path.Combine(settings.RepoRoot, "src/Payments.Mcp/bin/Debug/net10.0/Payments.Mcp.dll"),
            ],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["PAYMENTS_API_URL"] = settings.ApiUrl,
                ["PAYMENTS_API_KEY"] = agent.ApiKey,
            },
        });

        var mcp = await McpClient.CreateAsync(transport);

        var tools = await McpToolHost.CreateAsync(mcp);

        // The credential cannot be empty even though llama-server never checks it.
        var model = new OpenAI.Chat.ChatClient(
                settings.LlmModel,
                new ApiKeyCredential("none"),
                new OpenAIClientOptions { Endpoint = new Uri(settings.LlmUrl) })
            .AsIChatClient();

        // The printing and the crash are decorators, in that order. The loop never learns that
        // anything is watching it or that the process can die.
        IAgentRunStore store = new PrintingStore(OpenStore());

        if (crashAfter is int after and > 0)
        {
            store = new CrashingStore(store, after);
        }

        var loop = new AgentLoop(
            model,
            tools,
            new ConsoleApprovalChannel(),
            store,
            agent.Options,
            TimeProvider.System);

        return new AgentHost(mcp, loop);
    }

    /// <summary>
    /// Disposing the MCP client is what ends the child process. Miss it and an orphan
    /// <c>Payments.Mcp.dll</c> is left running after every run.
    /// </summary>
    public ValueTask DisposeAsync() => _mcp.DisposeAsync();
}
