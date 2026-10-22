using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Payments.Mcp;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

    // Everything we log goes to stderr, because stdout is the protocol.
    builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

    // Warnings and worse only. A client is free to ignore stderr, and a client that
    // ignores it never drains that pipe. A full pipe blocks the next write, and a
    // blocked write freezes the server mid-call: the MCP Inspector then shows
    // "request timed out" for ever. Four info lines per tool call fill it; warnings
    // alone cannot. The threshold above still decides the stream, this decides the
    // volume.
    builder.Logging.SetMinimumLevel(LogLevel.Warning);

    builder.Services.AddPaymentsMcp(builder.Configuration)
        .WithStdioServerTransport();
    
await builder.Build().RunAsync();