using System.Text.Json;
using SqlServerMcpServer.Configuration;

namespace SqlServerMcpServer.Utilities
{
    /// <summary>
    /// Provides standardized response formatting for SQL Server operations
    /// </summary>
    public static class ResponseFormatter
    {
        /// <summary>
        /// Creates an error response from ErrorContext
        /// </summary>
        /// <param name="context">The error context</param>
        /// <param name="executionTimeMs">Execution time in milliseconds</param>
        /// <returns>Standard error response object</returns>
        public static object CreateErrorContextResponse(ErrorContext context, long executionTimeMs = 0)
        {
            return new
            {
                server_name = SqlConnectionManager.ServerName,
                environment = SqlConnectionManager.Environment,
                database = SqlConnectionManager.CurrentDatabase,
                operation = context.Operation,
                timestamp = DateTimeOffset.UtcNow,
                execution_time_ms = executionTimeMs,
                security_mode = "READ_ONLY_ENFORCED",
                error = new
                {
                    code = context.Code.GetDescription(),
                    message = context.Message,
                    sql_error = context.SqlErrorNumber.HasValue ? new
                    {
                        error_number = context.SqlErrorNumber,
                        line_number = context.SqlErrorLineNumber
                    } : null,
                    can_retry = context.CanRetry(),
                    is_transient = context.IsTransient,
                    details = context.Details,
                    troubleshooting_steps = context.TroubleshootingSteps ?? new List<string>(),
                    suggested_fixes = context.SuggestedFixes ?? new List<string>(),
                    documentation_links = context.DocumentationLinks ?? new List<string>()
                }
            };
        }

        /// <summary>
        /// Creates a standard success response
        /// </summary>
        /// <param name="operation">The operation name</param>
        /// <param name="data">The response data</param>
        /// <param name="executionTimeMs">Execution time in milliseconds</param>
        /// <param name="warnings">Optional warnings</param>
        /// <param name="recommendations">Optional recommendations</param>
        /// <param name="metadata">Optional metadata</param>
        /// <returns>Standard response object</returns>
        public static object CreateStandardResponse(string operation, object data, long executionTimeMs,
            List<string> warnings = null, List<string> recommendations = null,
            Dictionary<string, object> metadata = null)
        {
            return new
            {
                server_name = SqlConnectionManager.ServerName,
                environment = SqlConnectionManager.Environment,
                database = SqlConnectionManager.CurrentDatabase,
                operation = operation,
                timestamp = DateTimeOffset.UtcNow,
                execution_time_ms = executionTimeMs,
                security_mode = "READ_ONLY",
                data = data,
                metadata = metadata ?? new Dictionary<string, object>(),
                warnings = warnings ?? new List<string>(),
                recommendations = recommendations ?? new List<string>()
            };
        }

        /// <summary>
        /// Creates a standard error response
        /// </summary>
        /// <param name="operation">The operation name</param>
        /// <param name="error">The error message</param>
        /// <param name="executionTimeMs">Execution time in milliseconds</param>
        /// <param name="errorCode">Error code</param>
        /// <param name="errorDetails">Additional error details</param>
        /// <param name="troubleshootingSteps">Troubleshooting steps</param>
        /// <returns>Standard error response object</returns>
        public static object CreateStandardErrorResponse(string operation, string error, long executionTimeMs = 0,
            string errorCode = "SQL_ERROR", Dictionary<string, object> errorDetails = null,
            List<string> troubleshootingSteps = null)
        {
            return new
            {
                server_name = SqlConnectionManager.ServerName,
                environment = SqlConnectionManager.Environment,
                database = SqlConnectionManager.CurrentDatabase,
                operation = operation,
                timestamp = DateTimeOffset.UtcNow,
                execution_time_ms = executionTimeMs,
                security_mode = "READ_ONLY_ENFORCED",
                error = new
                {
                    code = errorCode,
                    message = error,
                    details = errorDetails,
                    troubleshooting_steps = troubleshootingSteps ?? new List<string>
                    {
                        "Check your SQL syntax",
                        "Verify object names and permissions",
                        "Ensure you're using only SELECT statements"
                    }
                }
            };
        }

        /// <summary>
        /// Creates a standard blocked operation response
        /// </summary>
        /// <param name="operation">The operation name</param>
        /// <param name="blockedOperation">The blocked operation type</param>
        /// <param name="query">The blocked query</param>
        /// <param name="errorMessage">Error message</param>
        /// <returns>Standard blocked response object</returns>
        public static object CreateStandardBlockedResponse(string operation, string blockedOperation,
            string query, string errorMessage)
        {
            return new
            {
                server_name = SqlConnectionManager.ServerName,
                environment = SqlConnectionManager.Environment,
                database = SqlConnectionManager.CurrentDatabase,
                operation = operation,
                timestamp = DateTimeOffset.UtcNow,
                security_mode = "READ_ONLY_ENFORCED",
                error = new
                {
                    code = "BLOCKED_OPERATION",
                    message = errorMessage,
                    details = new Dictionary<string, object>
                    {
                        ["blocked_operation"] = blockedOperation,
                        ["blocked_query"] = query
                    },
                    troubleshooting_steps = new List<string>
                    {
                        "This MCP server is READ-ONLY only",
                        "Use SELECT statements for data retrieval",
                        "Database listing, schema inspection, and switching are allowed"
                    }
                }
            };
        }

        /// <summary>
        /// Serializes an object to JSON with indented formatting
        /// </summary>
        /// <param name="obj">Object to serialize</param>
        /// <returns>JSON string</returns>
        public static string ToJson(object obj)
        {
            return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>
        /// Gets a suggestion for a SQL error number
        /// </summary>
        /// <param name="errorNumber">SQL Server error number</param>
        /// <returns>Suggestion message</returns>
        public static string GetErrorSuggestion(int errorNumber)
        {
            return errorNumber switch
            {
                102 => "Incorrect syntax near a keyword. Check your SQL syntax.",
                207 => "Invalid column name. Verify column names exist in the table.",
                208 => "Invalid object name. Check if table/view exists and schema is correct.",
                245 => "Conversion failed. Check data types in your WHERE clause.",
                815 => "Arithmetic overflow. Check numeric values in your query.",
                156 => "Incorrect syntax near the keyword. Check for missing commas or parentheses.",
                137 => "Must declare the scalar variable. Check parameter usage.",
                2812 => "Could not find stored procedure. Verify procedure name and schema.",
                911 => "Database does not exist. Check database name.",
                18456 => "Login failed. Check connection credentials.",
                4060 => "Cannot open database. Check database name and permissions.",
                _ => "Check SQL syntax, object names, and permissions."
            };
        }

        /// <summary>
        /// Creates a blocked response from ErrorContext
        /// </summary>
        /// <param name="context">The error context with blocked operation details</param>
        /// <param name="executionTimeMs">Execution time in milliseconds</param>
        /// <returns>Standard blocked response object</returns>
        public static object CreateBlockedContextResponse(ErrorContext context, long executionTimeMs = 0)
        {
            return new
            {
                server_name = SqlConnectionManager.ServerName,
                environment = SqlConnectionManager.Environment,
                database = SqlConnectionManager.CurrentDatabase,
                operation = context.Operation,
                timestamp = DateTimeOffset.UtcNow,
                execution_time_ms = executionTimeMs,
                security_mode = "READ_ONLY_ENFORCED",
                error = new
                {
                    code = context.Code.GetDescription(),
                    message = context.Message,
                    details = context.Details,
                    troubleshooting_steps = context.TroubleshootingSteps ?? new List<string>(),
                    suggested_fixes = context.SuggestedFixes ?? new List<string>(),
                    documentation_links = context.DocumentationLinks ?? new List<string>()
                }
            };
        }
    }
}
