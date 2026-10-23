using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace Payments.Api.Tests;

/// <summary>
/// The real MCP server, in this process, with its API client wired to the real API on the
/// real throwaway Postgres. Nothing is stubbed: a tool call runs tool, fence, API client,
/// key filter, endpoint, Dapper and Postgres, so the proof of a refund is a row.
/// </summary>
/// <remarks>
/// The pipes, the host and the client live in <see cref="InMemoryMcpServer"/> (lab 09 step 3,
/// <c>CF-033</c>). What is left here is what only this harness has: the fixture it borrows the
/// API handler from, and the optional broken network.
/// </remarks>
internal sealed class McpEndToEndHarness : IAsyncDisposable
{
    private InMemoryMcpServer _server = null!;

    public McpClient Client => _server.Client;

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

        harness._server = await InMemoryMcpServer.StartAsync(
            new Dictionary<string, string?>
            {
                ["PAYMENTS_API_URL"] = fixture.Factory.Server.BaseAddress.ToString(),
                ["PAYMENTS_API_KEY"] = apiKey,
            },

            // The one line that makes this end to end: the API client's socket is the
            // test server itself, so there is no port, no network and no stub.
            api =>
            {
                api.ConfigurePrimaryHttpMessageHandler(() => fixture.Factory.Server.CreateHandler());

                if (brokenNetwork is not null)
                {
                    api.AddHttpMessageHandler(() => brokenNetwork);
                }
            });

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
