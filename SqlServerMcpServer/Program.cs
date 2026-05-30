using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Configuration;
using Serilog;
using SqlServerMcpServer;

var transport = (Environment.GetEnvironmentVariable("MCP_TRANSPORT") ?? "stdio").ToLowerInvariant();

switch (transport)
{
    case "tcp":
        await RunTcpTransportAsync(args);
        break;
    case "sse":
    case "http":
        await RunSseTransportAsync(args);
        break;
    default:
        await RunStdioTransportAsync(args);
        break;
}


static async Task RunStdioTransportAsync(string[] args)
{
    ConfigureSerilog();
    var (builder, config) = ConfigureHost(args);
    var serverOptions = ConfigureMcpServer(config);
    builder.Services
        .AddMcpServer(serverOptions)
        .WithStdioServerTransport()
        .WithToolsFromAssembly(typeof(SqlServerTools).Assembly);
    var host = builder.Build();
    await host.RunAsync();
}

static async Task RunTcpTransportAsync(string[] args)
{
    ConfigureSerilog();
    var port = ParsePortEnv();
    var listener = new TcpListener(IPAddress.Any, port);
    listener.Start();
    Log.Information("[MCP-TCP] Listening on 0.0.0.0:{Port}", port);
    Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] MCP TCP transport listening on 0.0.0.0:{port}");

    while (true)
    {
        var client = await listener.AcceptTcpClientAsync();
        Log.Information("[MCP-TCP] Client connected from {Remote}", client.Client.RemoteEndPoint);
        Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] TCP client connected: {client.Client.RemoteEndPoint}");

        _ = Task.Run(async () =>
        {
            try
            {
                using var tcpClient = client;
                var stream = tcpClient.GetStream();
                var (builder, config) = ConfigureHost(args);
                var serverOptions = ConfigureMcpServer(config);
                builder.Services
                    .AddMcpServer(serverOptions)
                    .WithStreamServerTransport(stream, stream)
                    .WithToolsFromAssembly(typeof(SqlServerTools).Assembly);
                var host = builder.Build();
                await host.RunAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MCP-TCP] Session error: {Message}", ex.Message);
                Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ERROR] TCP session error: {ex.Message}");
            }
        });
    }
}


// ── SSE MCP transport (multi-session) ────────────────────────────────────

static async Task RunSseTransportAsync(string[] args)
{
    ConfigureSerilog();

    var port = ParsePortEnv();
    var sessions = new ConcurrentDictionary<string, SseSession>();

    // Bind on all interfaces via ASPNETCORE_URLS (Kestrel is the default)
    Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://0.0.0.0:{port}");
    var webBuilder = WebApplication.CreateBuilder(args);
    webBuilder.Logging.ClearProviders();

    var app = webBuilder.Build();

    // GET /sse — establish SSE stream, return session ID, then stream responses
    app.MapGet("/sse", async (HttpContext ctx) =>
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var session = new SseSession(sessionId);
        sessions.TryAdd(sessionId, session);

        try
        {
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";

            // First event: the endpoint URL where to POST messages
            var postUrl = $"/message?sessionId={sessionId}";
            await ctx.Response.WriteAsync($"event: endpoint\ndata: {postUrl}\n\n", ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

            // Start MCP server for this session on background
            var mcpCts = new CancellationTokenSource();
            var mcpTask = StartSessionMcpServer(session, mcpCts.Token, args);

            // Stream server→client messages as SSE events
            await foreach (var data in session.ServerToClient.Reader.ReadAllAsync(ctx.RequestAborted))
            {
                var text = System.Text.Encoding.UTF8.GetString(data);
                await ctx.Response.WriteAsync($"data: {text}\n\n", ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            // Session cleanup
            if (sessions.TryRemove(sessionId, out var removed))
            {
                removed.ClientToServer.Writer.TryComplete();
                removed.ServerToClient.Writer.TryComplete();
            }
        }
    });

    // POST /message?sessionId=xxx — send a JSON-RPC message to a session
    app.MapPost("/message", async (HttpContext ctx) =>
    {
        var sessionId = ctx.Request.Query["sessionId"].FirstOrDefault();
        if (string.IsNullOrEmpty(sessionId) || !sessions.TryGetValue(sessionId, out var session))
        {
            return Results.NotFound("Session not found. Establish an SSE connection first at GET /sse.");
        }

        var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync(ctx.RequestAborted);
        var bytes = System.Text.Encoding.UTF8.GetBytes(body + "\n");
        await session.ClientToServer.Writer.WriteAsync(bytes, ctx.RequestAborted);
        return Results.Ok(new { status = "accepted" });
    });

    app.MapGet("/health", () => Results.Ok(new { status = "healthy", transport = "sse", sessions = sessions.Count }));

    Log.Information("[MCP-SSE] HTTP server listening on 0.0.0.0:{Port}", port);
    Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] MCP SSE transport listening on http://0.0.0.0:{port}");

    await app.RunAsync();
}

static async Task StartSessionMcpServer(SseSession session, CancellationToken ct, string[] args)
{
    try
    {
        var (builder, config) = ConfigureHost(args);
        var serverOptions = ConfigureMcpServer(config);
        builder.Services
            .AddMcpServer(serverOptions)
            .WithStreamServerTransport(
                new ChannelInputStream(session.ClientToServer.Reader),
                new ChannelOutputStream(session.ServerToClient.Writer))
            .WithToolsFromAssembly(typeof(SqlServerTools).Assembly);
        var host = builder.Build();
        await host.RunAsync(ct);
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ERROR] MCP session {session.SessionId} failed: {ex.Message}");
        Log.Error(ex, "[MCP-SSE] Session {SessionId} failed: {Message}", session.SessionId, ex.Message);
    }
}


// ── Host / MCP server configuration ──────────────────────────────────────

static (HostApplicationBuilder builder, IConfiguration config) ConfigureHost(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
    builder.Logging.ClearProviders();
    builder.Logging.AddSerilog(Log.Logger, dispose: true);
    return (builder, builder.Configuration);
}

static Action<McpServerOptions> ConfigureMcpServer(IConfiguration? config)
{
    var serverDisplayName = Environment.GetEnvironmentVariable("MCP_SERVER_NAME")
        ?? config?["MCP_SERVER_NAME"]
        ?? "SQL Server MCP";
    var serverVersion = typeof(SqlServerTools).Assembly.GetName().Version is { } version
        ? $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}"
        : "1.0.0";

    return options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = serverDisplayName,
            Title = serverDisplayName,
            Version = serverVersion,
            Description = "SQL Server MCP server for schema inspection, diagnostics, and query execution with safety checks."
        };
        options.ServerInstructions = "Use these tools to inspect SQL Server schemas, metadata, diagnostics, and query results. Prefer targeted schema tools before broad queries. This server is intended for read-only workflows, and write-oriented SQL should be treated as blocked for safety.";
    };
}

static void ConfigureSerilog()
{
    var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
    Directory.CreateDirectory(logDirectory);
    var logPath = Path.Combine(logDirectory, "mcp-server-.log");

    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console(
            standardErrorFromLevel: Serilog.Events.LogEventLevel.Information,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(logPath,
            rollingInterval: RollingInterval.Day,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}",
            retainedFileCountLimit: 7)
        .CreateLogger();
}

static int ParsePortEnv()
{
    var portEnv = Environment.GetEnvironmentVariable("MCP_PORT");
    if (int.TryParse(portEnv, out var p) && p > 0 && p <= 65535)
        return p;
    return 8080;
}
