using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Payments.Mcp;

public static class McpSetup
{
    public static IMcpServerBuilder AddPaymentsMcp(this IServiceCollection services, IConfiguration config)
        => services.AddMcpServer();
}