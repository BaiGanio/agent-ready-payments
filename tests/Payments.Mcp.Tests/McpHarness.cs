using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Payments.Mcp.Tests;

/// <summary>
/// Runs the real MCP server inside the test process and talks to it with the real
/// client over two in-memory pipes. Every test goes through the protocol.
/// </summary>
public sealed class McpHarness : IAsyncDisposable
{
    private IHost _host = null!;

    public const string ApiKey = "test-key-123";

    public McpClient Client { get; private set; } = null!;

    public FakeTimeProvider Clock { get; } = new();

    public StubApi Api { get; } = new();

    /// <param name="apiKey">
    /// The value of PAYMENTS_API_KEY. Pass "" or null to start with no key: the in-memory
    /// source is added last, so it wins over a real environment variable and the test holds
    /// even when your shell has the key set.
    /// </param>
    public static async Task<McpHarness> StartAsync(string? apiKey = ApiKey)
    {
        var harness = new McpHarness();

        // Two one-way hoses. The ends are crossed on purpose.
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PAYMENTS_API_URL"] = "http://api.test/",
            ["PAYMENTS_API_KEY"] = apiKey ?? string.Empty,
        });

        builder.Services.AddSingleton<TimeProvider>(harness.Clock);

        // The stub sits behind the API client, so a test sees every request the tools send.
        // Step 13 registers that client; until then AddPaymentsMcp never calls this hook.
        builder.Services.AddPaymentsMcp(
                builder.Configuration,
                api => api.ConfigurePrimaryHttpMessageHandler(() => harness.Api))
            .WithStreamServerTransport(
                clientToServer.Reader.AsStream(),
                serverToClient.Writer.AsStream());

        harness._host = builder.Build();

        // The host must be running before the client tries to shake hands.
        await harness._host.StartAsync();

        harness.Client = await McpClient.CreateAsync(
            new StreamClientTransport(
                clientToServer.Writer.AsStream(),
                serverToClient.Reader.AsStream()));

        return harness;
    }

    /// <summary>
    /// Calls a tool and returns the single text block, parsed as JSON.
    /// </summary>
    public async Task<(bool IsError, JsonElement Body)> CallAsync(
        string tool, object? arguments = null)
    {
        var args = arguments is null
            ? new Dictionary<string, object?>()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(
                  JsonSerializer.Serialize(arguments))!;

        var result = await Client.CallToolAsync(tool, args);

        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        using var parsed = JsonDocument.Parse(text);

        return (result.IsError ?? false, parsed.RootElement.Clone());
    }

    public async ValueTask DisposeAsync()
    {
        if (Client is not null)
        {
            await Client.DisposeAsync();
        }

        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}
