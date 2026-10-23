using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Payments.Mcp;

namespace Payments.Testing;

/// <summary>
/// Runs the real MCP server inside the test process and talks to it with the real client
/// over two in-memory pipes. Every call goes through the protocol. What sits behind the
/// API client is the caller's choice: a <see cref="StubApi"/>, or the real API's handler.
/// </summary>
public sealed class InMemoryMcpServer : IAsyncDisposable
{
    private IHost _host = null!;

    public McpClient Client { get; private set; } = null!;

    /// <param name="config">
    /// PAYMENTS_API_URL and PAYMENTS_API_KEY. Added last, so it wins over a real
    /// environment variable and the test holds even when your shell has the key set.
    /// </param>
    /// <param name="api">Wires the API client's handler: the stub, or the test server's.</param>
    /// <param name="services">Extra registrations, such as a FakeTimeProvider.</param>
    public static async Task<InMemoryMcpServer> StartAsync(
        IReadOnlyDictionary<string, string?> config,
        Action<IHttpClientBuilder> api,
        Action<IServiceCollection>? services = null)
    {
        var server = new InMemoryMcpServer();

        // Two one-way hoses. The ends are crossed on purpose.
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(config);

        services?.Invoke(builder.Services);

        builder.Services.AddPaymentsMcp(builder.Configuration, api)
            .WithStreamServerTransport(
                clientToServer.Reader.AsStream(),
                serverToClient.Writer.AsStream());

        server._host = builder.Build();

        // The host must be running before the client tries to shake hands.
        await server._host.StartAsync();

        server.Client = await McpClient.CreateAsync(
            new StreamClientTransport(
                clientToServer.Writer.AsStream(),
                serverToClient.Reader.AsStream()));

        return server;
    }

    /// <summary>Calls a tool and returns the single text block, parsed as JSON.</summary>
    public async Task<(bool IsError, JsonElement Body)> CallAsync(string tool, object? arguments = null)
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
