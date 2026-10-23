using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Client;

namespace Payments.Mcp.Tests;

/// <summary>
/// The real MCP server over in-memory pipes, with a <see cref="StubApi"/> behind the API
/// client and a clock the test controls. The pipes, the host and the client live in
/// <see cref="InMemoryMcpServer"/>; this class owns only the two things that are its own.
/// </summary>
public sealed class McpHarness : IAsyncDisposable
{
    private InMemoryMcpServer _server = null!;

    public const string ApiKey = "test-key-123";

    public McpClient Client => _server.Client;

    public FakeTimeProvider Clock { get; } = new();

    public StubApi Api { get; } = new();

    /// <param name="apiKey">
    /// The value of PAYMENTS_API_KEY. Pass "" or null to start with no key.
    /// </param>
    public static async Task<McpHarness> StartAsync(string? apiKey = ApiKey)
    {
        var harness = new McpHarness();

        harness._server = await InMemoryMcpServer.StartAsync(
            new Dictionary<string, string?>
            {
                ["PAYMENTS_API_URL"] = "http://api.test/",
                ["PAYMENTS_API_KEY"] = apiKey ?? string.Empty,
            },

            // The stub sits behind the API client, so a test sees every request the tools send.
            api => api.ConfigurePrimaryHttpMessageHandler(() => harness.Api),

            services => services.AddSingleton<TimeProvider>(harness.Clock));

        return harness;
    }

    /// <summary>Calls a tool and returns the single text block, parsed as JSON.</summary>
    public Task<(bool IsError, JsonElement Body)> CallAsync(string tool, object? arguments = null)
        => _server.CallAsync(tool, arguments);

    public async ValueTask DisposeAsync()
    {
        if (_server is not null) await _server.DisposeAsync();
    }
}
