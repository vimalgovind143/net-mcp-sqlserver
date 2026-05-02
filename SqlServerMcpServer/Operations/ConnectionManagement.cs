using Microsoft.Data.SqlClient;
using SqlServerMcpServer.Configuration;
using SqlServerMcpServer.Utilities;
using System.Diagnostics;

namespace SqlServerMcpServer.Operations
{
    /// <summary>
    /// Implements the three connection-management MCP tools:
    /// GetConnections, AddConnection, SwitchConnection.
    /// </summary>
    public static class ConnectionManagement
    {
        // ── GetConnections ────────────────────────────────────────────────────

        /// <summary>
        /// Returns all registered connections and which one is currently active.
        /// </summary>
        public static string GetConnections()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("GetConnections", "listing all connections");

                var allConnections = SqlConnectionManager.GetAllConnections();
                var activeN        = SqlConnectionManager.GetActiveConnectionName();

                var list = allConnections.Select(c => new
                {
                    name       = c.Name,
                    server     = c.ServerName,
                    database   = c.CurrentDatabase,
                    is_active  = c.IsActive,
                    created_at = c.CreatedAt,
                    last_used  = c.LastUsed
                }).ToList();

                var data = new
                {
                    connections        = list,
                    active_connection  = activeN,
                    total_connections  = list.Count
                };

                var payload = ResponseFormatter.CreateStandardResponse(
                    "GetConnections", data, sw.ElapsedMilliseconds);

                LoggingHelper.LogEnd(corr, "GetConnections", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "GetConnections");
                LoggingHelper.LogEnd(Guid.Empty, "GetConnections", false, sw.ElapsedMilliseconds, ex.Message);
                return ResponseFormatter.ToJson(ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds));
            }
        }

        // ── AddConnection ─────────────────────────────────────────────────────

        /// <summary>
        /// Registers a new named SQL Server connection.
        /// Always tests connectivity before adding.
        /// </summary>
        public static async Task<string> AddConnectionAsync(
            string name,
            string connectionString,
            bool setAsActive = false)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("AddConnection", $"name={name}, setAsActive={setAsActive}");

                // Validate parameters
                if (string.IsNullOrWhiteSpace(name))
                {
                    var ctx = new ErrorContext(ErrorCode.InvalidParameter, "Connection name cannot be empty.", "AddConnection");
                    ctx.SuggestedFixes.Add("Provide a short, unique name such as 'prod', 'reporting', 'staging'.");
                    return ResponseFormatter.ToJson(ResponseFormatter.CreateErrorContextResponse(ctx, sw.ElapsedMilliseconds));
                }

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    var ctx = new ErrorContext(ErrorCode.InvalidParameter, "Connection string cannot be empty.", "AddConnection");
                    ctx.SuggestedFixes.Add("Example: Server=myhost;Database=mydb;User Id=sa;Password=...;TrustServerCertificate=true;");
                    return ResponseFormatter.ToJson(ResponseFormatter.CreateErrorContextResponse(ctx, sw.ElapsedMilliseconds));
                }

                // Test connectivity
                string serverName;
                string databaseName;
                try
                {
                    using var testConn = new SqlConnection(connectionString);
                    await testConn.OpenAsync();

                    // Read actual server / database from the live connection
                    serverName   = testConn.DataSource;
                    databaseName = testConn.Database;
                }
                catch (SqlException sqlEx)
                {
                    sw.Stop();
                    var ctx = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "AddConnection");
                    ctx.SuggestedFixes.Add("Verify the server name, port, credentials and network connectivity.");
                    ctx.SuggestedFixes.Add("For Azure SQL / always-encrypted servers, include TrustServerCertificate=true.");
                    LoggingHelper.LogEnd(Guid.Empty, "AddConnection", false, sw.ElapsedMilliseconds, sqlEx.Message);
                    return ResponseFormatter.ToJson(ResponseFormatter.CreateErrorContextResponse(ctx, sw.ElapsedMilliseconds));
                }

                // Register in the manager
                SqlConnectionManager.AddConnection(name, connectionString, testConnection: false, setAsActive: setAsActive);

                var data = new
                {
                    name,
                    server         = serverName,
                    database       = databaseName,
                    is_active      = setAsActive,
                    registered_at  = DateTime.UtcNow,
                    message        = setAsActive
                        ? $"Connection '{name}' added and set as active."
                        : $"Connection '{name}' added. Call SwitchConnection to make it active.",
                    available_connections = SqlConnectionManager.GetConnectionNames()
                };

                var payload = ResponseFormatter.CreateStandardResponse("AddConnection", data, sw.ElapsedMilliseconds);
                LoggingHelper.LogEnd(corr, "AddConnection", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "AddConnection");
                LoggingHelper.LogEnd(Guid.Empty, "AddConnection", false, sw.ElapsedMilliseconds, ex.Message);
                return ResponseFormatter.ToJson(ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds));
            }
        }

        // ── SwitchConnection ──────────────────────────────────────────────────

        /// <summary>
        /// Switches the active SQL Server connection to a different named instance.
        /// </summary>
        public static string SwitchConnection(string name)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("SwitchConnection", $"name={name}");

                if (string.IsNullOrWhiteSpace(name))
                {
                    var ctx = new ErrorContext(ErrorCode.InvalidParameter, "Connection name cannot be empty.", "SwitchConnection");
                    ctx.SuggestedFixes.Add($"Available connections: {string.Join(", ", SqlConnectionManager.GetConnectionNames())}");
                    return ResponseFormatter.ToJson(ResponseFormatter.CreateErrorContextResponse(ctx, sw.ElapsedMilliseconds));
                }

                if (!SqlConnectionManager.ConnectionExists(name))
                {
                    var ctx = new ErrorContext(ErrorCode.InvalidParameter,
                        $"Connection '{name}' not found.", "SwitchConnection");
                    ctx.SuggestedFixes.Add($"Available connections: {string.Join(", ", SqlConnectionManager.GetConnectionNames())}");
                    ctx.SuggestedFixes.Add("Use AddConnection to register a new connection first.");
                    return ResponseFormatter.ToJson(ResponseFormatter.CreateErrorContextResponse(ctx, sw.ElapsedMilliseconds));
                }

                SqlConnectionManager.SwitchConnection(name);
                var active = SqlConnectionManager.GetConnection(name);

                var data = new
                {
                    active_connection     = name,
                    server                = active.ServerName,
                    database              = active.CurrentDatabase,
                    message               = $"Active connection switched to '{name}'.",
                    available_connections = SqlConnectionManager.GetConnectionNames()
                };

                var payload = ResponseFormatter.CreateStandardResponse("SwitchConnection", data, sw.ElapsedMilliseconds);
                LoggingHelper.LogEnd(corr, "SwitchConnection", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "SwitchConnection");
                LoggingHelper.LogEnd(Guid.Empty, "SwitchConnection", false, sw.ElapsedMilliseconds, ex.Message);
                return ResponseFormatter.ToJson(ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds));
            }
        }
    }
}
