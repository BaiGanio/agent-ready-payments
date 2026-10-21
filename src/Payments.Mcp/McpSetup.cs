using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Payments.Mcp;

public static class McpSetup
{
     public static IMcpServerBuilder AddPaymentsMcp(this IServiceCollection services, IConfiguration config)
        => services.AddMcpServer()
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