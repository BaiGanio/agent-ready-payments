using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Payments.Mcp;

namespace Payments.Api.Tests;

/// <summary>
/// The real MCP server, in this process, with its API client wired to the real API on the
/// real throwaway Postgres. Nothing is stubbed: a tool call runs tool, fence, API client,
/// key filter, endpoint, Dapper and Postgres, so the proof of a refund is a row.
/// </summary>
/// <remarks>
/// This is the second harness in the repo. <c>Payments.Mcp.Tests/McpHarness.cs</c> is the
/// first, and the pipes, the transport and the client are the same three steps in both.
/// It is not shared: that one owns a <c>StubApi</c> and a <c>FakeTimeProvider</c>, this one
/// owns an <c>ApiFixture</c>, and the two test projects do not reference each other.
/// </remarks>
internal sealed class McpEndToEndHarness : IAsyncDisposable
{
    private IHost _host = null!;

    public McpClient Client { get; private set; } = null!;

    /// <param name="fixture">Gives the base address and the in-process handler of the real API.</param>
    /// <param name="apiKey">The raw API key the server sends as X-Api-Key. It decides the merchant and the scopes.</param>
    /// <param name="brokenNetwork">
    /// An optional handler spliced into the API client's chain, to break the network on purpose.
    /// It sits outside the primary handler, so it sees every call the server makes and can let one
    /// through and then lose the answer.
    /// </param>
    public static async Task<McpEndToEndHarness> StartAsync(
        ApiFixture fixture, string apiKey, DelegatingHandler? brokenNetwork = null)
    {
        var harness = new McpEndToEndHarness();

        // Two one-way hoses. The ends are crossed on purpose.
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var builder = Host.CreateApplicationBuilder();

        // Added last, so it wins over a real environment variable on this machine.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PAYMENTS_API_URL"] = fixture.Factory.Server.BaseAddress.ToString(),
            ["PAYMENTS_API_KEY"] = apiKey,
        });

        builder.Services.AddPaymentsMcp(
                builder.Configuration,

                // The one line that makes this end to end: the API client's socket is the
                // test server itself, so there is no port, no network and no stub.
                api =>
                {
                    api.ConfigurePrimaryHttpMessageHandler(() => fixture.Factory.Server.CreateHandler());

                    if (brokenNetwork is not null)
                    {
                        api.AddHttpMessageHandler(() => brokenNetwork);
                    }
                })
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
