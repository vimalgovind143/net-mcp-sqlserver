using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Configuration;
using Serilog;
using SqlServerMcpServer;

// Configure Serilog for file-only logging
// Use AppContext.BaseDirectory to ensure logs are written relative to the executable location
var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logDirectory);
var logPath = Path.Combine(logDirectory, "mcp-server-.log");

Log.Logger = new LoggerConfiguration()
    .WriteTo.File(logPath, 
        rollingInterval: RollingInterval.Day,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}",
        retainedFileCountLimit: 7)
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);

// Load JSON configuration if present (appsettings.json)
builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

// Configure file-only logging
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger, dispose: true);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var host = builder.Build();

try
{
    await host.RunAsync();
}
catch (Exception ex)
{
    // Only log to file, don't write to console to avoid MCP interference
    Log.Error(ex, "Host failed to start: {ErrorMessage}", ex.Message);
    throw;
}
finally
{
    Log.CloseAndFlush();
}
