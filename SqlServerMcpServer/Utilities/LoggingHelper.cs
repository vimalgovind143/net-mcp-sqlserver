using Serilog;
using SqlServerMcpServer.Configuration;

namespace SqlServerMcpServer.Utilities
{
    /// <summary>
    /// Provides logging functionality for SQL Server operations
    /// </summary>
    public static class LoggingHelper
    {
        /// <summary>
        /// Logs a JSON payload
        /// </summary>
        /// <param name="payload">Object to log</param>
        public static void LogJson(object payload)
        {
            try
            {
                Log.Information("{@payload}", payload);
            }
            catch { }
        }

        /// <summary>
        /// Logs the start of an operation
        /// </summary>
        /// <param name="operation">Operation name</param>
        /// <param name="context">Optional context</param>
        /// <returns>Correlation ID for the operation</returns>
        public static Guid LogStart(string operation, string? context = null)
        {
            var id = Guid.NewGuid();
            LogJson(new
            {
                event_type = "start",
                correlation_id = id,
                operation,
                server_name = SqlConnectionManager.ServerName,
                environment = SqlConnectionManager.Environment,
                database = SqlConnectionManager.CurrentDatabase,
                context,
                timestamp = DateTimeOffset.UtcNow
            });
            return id;
        }

        /// <summary>
        /// Logs the end of an operation
        /// </summary>
        /// <param name="correlationId">Correlation ID from LogStart</param>
        /// <param name="operation">Operation name</param>
        /// <param name="success">Whether operation succeeded</param>
        /// <param name="elapsedMs">Elapsed time in milliseconds</param>
        /// <param name="error">Error message if operation failed</param>
        public static void LogEnd(Guid correlationId, string operation, bool success, long elapsedMs, string? error = null)
        {
            LogJson(new
            {
                event_type = "end",
                correlation_id = correlationId,
                operation,
                success,
                elapsed_ms = elapsedMs,
                server_name = SqlConnectionManager.ServerName,
                environment = SqlConnectionManager.Environment,
                database = SqlConnectionManager.CurrentDatabase,
                error,
                timestamp = DateTimeOffset.UtcNow
            });
        }
    }
}