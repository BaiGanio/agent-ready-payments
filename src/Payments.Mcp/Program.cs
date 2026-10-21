using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Payments.Mcp;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
    builder.Services.AddPaymentsMcp(builder.Configuration)
        .WithStdioServerTransport();
    
await builder.Build().RunAsync();