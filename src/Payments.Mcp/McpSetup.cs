using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Payments.Mcp;

public static class McpSetup
{
     /// <param name="configureApiClient">
     /// A hook on the API client's builder. The tests use it to put the stub handler behind the
     /// client, and step 18 uses it to point at the real API. Step 13 registers the client and
     /// passes the builder here.
     /// </param>
     public static IMcpServerBuilder AddPaymentsMcp(
        this IServiceCollection services,
        IConfiguration config,
        Action<IHttpClientBuilder>? configureApiClient = null)
    {
        // A missing key must stop the server at start-up, with the variable named.
        services.AddOptions<PaymentsApiOptions>()
            .Configure(options =>
            {
                options.Url = config["PAYMENTS_API_URL"] ?? string.Empty;
                options.ApiKey = config["PAYMENTS_API_KEY"] ?? string.Empty;
            })
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ApiKey),
                "PAYMENTS_API_KEY is not set. The MCP server needs its own API key for the payments API.")
            .Validate(
                options => Uri.TryCreate(options.Url, UriKind.Absolute, out _),
                "PAYMENTS_API_URL is not set to an absolute URL, such as http://localhost:5080/.")
            .ValidateOnStart();

        // The base address and the key are set here, once. No tool can forget them.
        var apiClient = services.AddHttpClient<PaymentsApi>((provider, http) =>
        {
            var options = provider.GetRequiredService<IOptions<PaymentsApiOptions>>().Value;

            http.BaseAddress = new Uri(options.Url, UriKind.Absolute);
            http.Timeout = TimeSpan.FromSeconds(10);
            http.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
        });

        // The tests put the stub behind the client here; step 18 points it at the real API.
        configureApiClient?.Invoke(apiClient);

        // A singleton: one token store per server process. A scoped one would be new
        // for every request, so the confirm would never find the proposal's token.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ConfirmationTokens>();

        return services.AddMcpServer()
            .WithTools<PaymentTools>()
            .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
            {
                // One fence for every tool. It reads the tool's own schema, so a tool
                // added tomorrow is fenced for free, with no list of names to keep in step.
                if (context.MatchedPrimitive is McpServerTool tool &&
                    context.Params?.Arguments is { Count: > 0 } sent)
                {
                    var known = tool.ProtocolTool.InputSchema.TryGetProperty("properties", out var properties)
                        ? properties.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal)
                        : [];

                    var unknown = sent.Keys.Where(name => !known.Contains(name)).Order().ToArray();

                    if (unknown.Length > 0)
                    {
                        // Dropping it silently is the dangerous case: the model sends
                        // merchant: "M1 Hotel", gets everybody's payments, and believes
                        // the list is filtered.
                        return PaymentTools.Failed(
                            "unknown_argument",
                            $"{tool.ProtocolTool.Name} has no argument named {string.Join(", ", unknown)}.",
                            $"The arguments it does take are: {string.Join(", ", known.Order())}.");
                    }
                }

                return await next(context, cancellationToken);
            }));
    }
}
