using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Payments.Agent.Tools;

namespace Payments.Agent.Tests;

/// <summary>
/// One line to get a loop that is fake where it has to be and real where it matters.
/// <para>
/// The model is scripted, the store is a dictionary, the human is instant and the clock is
/// ours. The <b>tools are real</b>: the true <c>Payments.Mcp</c> server runs in this process
/// over two pipes, so every call goes through the real MCP protocol and the real argument
/// fence. A fake tool host would let the loop pass tests that the live server fails.
/// </para>
/// </summary>
public sealed class AgentHarness : IAsyncDisposable
{
    private InMemoryMcpServer _mcp = null!;

    public ScriptedChatClient Model { get; } = new();

    public StubApi Api { get; } = new();

    public InMemoryAgentRunStore Store { get; } = new();

    public FakeApprovalChannel Approvals { get; private set; } = null!;

    public FakeTimeProvider Clock { get; } = new();

    public McpToolHost Tools { get; private set; } = null!;

    public AgentLoop Loop { get; private set; } = null!;

    /// <summary>
    /// The loop, wired up. <paramref name="wrapTools"/> puts something between the loop and the
    /// real MCP host: one test needs a tool that throws, and only a wrapper can break one tool
    /// while every other tool and the whole tool list stay real.
    /// </summary>
    public static async Task<AgentHarness> StartAsync(
        AgentOptions? options = null,
        Func<IToolHost, IToolHost>? wrapTools = null)
    {
        var harness = new AgentHarness();

        harness._mcp = await InMemoryMcpServer.StartAsync(
            new Dictionary<string, string?>
            {
                ["PAYMENTS_API_URL"] = "http://api.test/",
                ["PAYMENTS_API_KEY"] = "test-key-123",
            },
            api => api.ConfigurePrimaryHttpMessageHandler(() => harness.Api),
            services => services.AddSingleton<TimeProvider>(harness.Clock));

        harness.Tools = await McpToolHost.CreateAsync(harness._mcp.Client);
        harness.Approvals = new FakeApprovalChannel(harness.Store);

        harness.Loop = new AgentLoop(
            harness.Model,
            wrapTools is null ? harness.Tools : wrapTools(harness.Tools),
            harness.Approvals,
            harness.Store,
            options ?? new AgentOptions { AgentName = "support-bot" },
            harness.Clock);

        return harness;
    }

    public async ValueTask DisposeAsync()
    {
        if (_mcp is not null)
        {
            await _mcp.DisposeAsync();
        }
    }
}
