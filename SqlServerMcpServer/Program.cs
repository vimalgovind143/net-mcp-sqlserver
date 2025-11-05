using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Formatting.Json;
using SqlServerMcpServer;

var builder = Host.CreateApplicationBuilder(args);

// Load JSON configuration if present (appsettings.json)
builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Sink(new StderrJsonSink(renderMessage: false))
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger, dispose: true);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

Console.WriteLine("Starting SQL Server MCP Server...");

await builder.Build().RunAsync();
