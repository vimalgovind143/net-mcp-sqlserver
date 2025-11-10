using Microsoft.Data.SqlClient;
using System.ComponentModel;
using System.Reflection;

namespace SqlServerMcpServer.Utilities
{
    /// <summary>
    /// Provides comprehensive error handling and context-aware error suggestions
    /// </summary>
    public static class ErrorHelper
    {
        /// <summary>
        /// Maps a SQL Server error number to an ErrorCode and creates ErrorContext
        /// </summary>
        /// <param name="sqlException">The SQL exception</param>
        /// <param name="operation">The operation being performed</param>
        /// <param name="query">The query that caused the error (optional)</param>
        /// <returns>ErrorContext with detailed information</returns>
        public static ErrorContext CreateErrorContextFromSqlException(SqlException sqlException, string operation, string query = null)
        {
            if (sqlException?.Errors.Count == 0)
            {
                return new ErrorContext(ErrorCode.UnknownError, "Unknown SQL error occurred", operation);
            }

            var firstError = sqlException.Errors[0];
            var errorNumber = firstError.Number;
            var (errorCode, message, suggestions) = MapSqlErrorNumber(errorNumber, firstError.Message);

            var context = new ErrorContext(errorCode, message, operation)
            {
                SqlErrorNumber = errorNumber,
                SqlErrorLineNumber = firstError.LineNumber,
                InnerException = sqlException.Message,
                Query = query,
                IsTransient = IsTransientSqlError(errorNumber)
            };

            // Add SQL error-specific troubleshooting steps
            AddSqlErrorTroubleshootingSteps(context, errorNumber);

            // Add suggested fixes
            foreach (var suggestion in suggestions)
            {
                context.SuggestedFixes.Add(suggestion);
            }

            // Add documentation links
            AddDocumentationLinks(context, errorNumber);

            // Add details
            context.Details["sql_error_number"] = errorNumber;
            context.Details["sql_error_line"] = firstError.LineNumber;
            context.Details["sql_server"] = firstError.Server;
            context.Details["procedure"] = firstError.Procedure;

            return context;
        }

        /// <summary>
        /// Creates an ErrorContext from a general exception
        /// </summary>
        /// <param name="exception">The exception</param>
        /// <param name="operation">The operation being performed</param>
        /// <returns>ErrorContext with detailed information</returns>
        public static ErrorContext CreateErrorContextFromException(Exception exception, string operation)
        {
            var errorCode = ErrorCode.InternalError;
            var message = exception.Message;

            // Try to identify specific exception types
            if (exception is InvalidOperationException)
            {
                errorCode = ErrorCode.OperationFailed;
                message = "An invalid operation was attempted. Check your parameters and retry.";
            }
            else if (exception is ArgumentException)
            {
                errorCode = ErrorCode.InvalidParameter;
                message = "An invalid parameter was provided.";
            }
            else if (exception is TimeoutException)
            {
                errorCode = ErrorCode.QueryTimeout;
                message = "The operation timed out. Try with a longer timeout or simpler query.";
            }
            else if (exception is IOException)
            {
                errorCode = ErrorCode.SystemError;
                message = "An I/O error occurred. Check server resources and retry.";
            }

            var context = new ErrorContext(errorCode, message, operation)
            {
                InnerException = exception.Message,
                IsTransient = IsTransientException(exception)
            };

            if (exception.InnerException != null)
            {
                context.Details["inner_exception"] = exception.InnerException.Message;
            }

            return context;
        }

        /// <summary>
        /// Maps SQL error numbers to error codes with suggestions
        /// </summary>
        private static (ErrorCode code, string message, List<string> suggestions) MapSqlErrorNumber(int errorNumber, string defaultMessage)
        {
            return errorNumber switch
            {
                // Syntax Errors
                102 => (ErrorCode.SqlSyntaxError,
                    "Incorrect syntax in your SQL query",
                    new List<string>
                    {
                        "Check for missing commas, parentheses, or keywords",
                        "Verify SQL Server T-SQL syntax is being used",
                        "Look for typos in table or column names"
                    }),

                156 => (ErrorCode.SqlSyntaxError,
                    "Syntax error near a keyword",
                    new List<string>
                    {
                        "Check for missing commas or parentheses",
                        "Verify keyword usage and placement",
                        "Ensure proper quote usage around strings"
                    }),

                // Object & Column Errors
                207 => (ErrorCode.InvalidColumn,
                    "Invalid column name",
                    new List<string>
                    {
                        "Verify the column exists in the table",
                        "Check for typos in the column name",
                        "Ensure you've specified the table alias or name if needed",
                        "Check case sensitivity settings if applicable"
                    }),

                208 => (ErrorCode.InvalidObject,
                    "Invalid object or table name",
                    new List<string>
                    {
                        "Verify the table exists in the current database",
                        "Check the table schema and name",
                        "Ensure you're connected to the correct database",
                        "Try using [schema].[table_name] format"
                    }),

                2812 => (ErrorCode.ProcedureNotFound,
                    "Could not find stored procedure",
                    new List<string>
                    {
                        "Verify the procedure exists and is accessible",
                        "Check the procedure name spelling",
                        "Ensure you have permission to execute the procedure",
                        "Note: Stored procedures cannot be executed in READ-ONLY mode"
                    }),

                // Data Type & Conversion Errors
                245 => (ErrorCode.ConversionFailed,
                    "Conversion failed during query execution",
                    new List<string>
                    {
                        "Check data types in your WHERE clause",
                        "Ensure date formats are compatible",
                        "Verify numeric values are not out of range",
                        "Use CAST or CONVERT for explicit type conversion"
                    }),

                206 => (ErrorCode.ConversionFailed,
                    "Operand type clash in operation",
                    new List<string>
                    {
                        "Ensure operands have compatible data types",
                        "Use CAST to convert between types",
                        "Check comparison operators for type mismatches"
                    }),

                815 => (ErrorCode.ArithmeticOverflow,
                    "Arithmetic overflow occurred",
                    new List<string>
                    {
                        "Check numeric values for overflow",
                        "Use data types with larger ranges (e.g., BIGINT instead of INT)",
                        "Review calculation operations for potential overflows"
                    }),

                // Variable & Parameter Errors
                137 => (ErrorCode.UndefinedVariable,
                    "Must declare the scalar variable",
                    new List<string>
                    {
                        "Declare all variables before use with DECLARE keyword",
                        "Check variable names for typos",
                        "Ensure variables are in scope for the current query"
                    }),

                // Database & Connection Errors
                911 => (ErrorCode.DatabaseNotFound,
                    "Database does not exist",
                    new List<string>
                    {
                        "Verify the database name is correct",
                        "Check that the database is online",
                        "Ensure you have permission to access the database",
                        "Use GetDatabases tool to see available databases"
                    }),

                18456 => (ErrorCode.LoginFailed,
                    "Login failed for user",
                    new List<string>
                    {
                        "Verify your connection credentials",
                        "Check if the user account is enabled",
                        "Ensure you have permission to access this server",
                        "Contact your database administrator if the issue persists"
                    }),

                4060 => (ErrorCode.CannotOpenDatabase,
                    "Cannot open database",
                    new List<string>
                    {
                        "Verify the database name is correct",
                        "Check if the database is in ONLINE state",
                        "Ensure you have permission to access the database",
                        "Try switching to an available database first"
                    }),

                // Connection & Timeout Errors
                -2 => (ErrorCode.ConnectionTimeout,
                    "Connection timeout expired",
                    new List<string>
                    {
                        "Check network connectivity to the SQL Server",
                        "Verify the server is responding",
                        "Try increasing the connection timeout value",
                        "Check SQL Server is running and accepting connections"
                    }),

                -1 => (ErrorCode.ExecutionError,
                    "Timeout expired. The timeout period elapsed",
                    new List<string>
                    {
                        "Simplify your query or add WHERE clauses to reduce result set",
                        "Use pagination (OFFSET/FETCH) to retrieve data in chunks",
                        "Consider indexing columns used in WHERE clauses",
                        "Increase the query timeout if the query is complex"
                    }),

                -3 => (ErrorCode.ExecutionError,
                    "The connection was not closed",
                    new List<string>
                    {
                        "Check connection pool settings",
                        "Ensure connections are properly disposed",
                        "Monitor for connection leaks in your application"
                    }),

                64 => (ErrorCode.ConnectionFailed,
                    "Communication link failure",
                    new List<string>
                    {
                        "Check network connectivity",
                        "Verify firewall settings",
                        "Ensure SQL Server is accessible on the specified port",
                        "Check for network timeouts or interruptions"
                    }),

                // Transaction & Locking Errors
                1205 => (ErrorCode.ExecutionError,
                    "Deadlock detected - your transaction was chosen as deadlock victim",
                    new List<string>
                    {
                        "Reduce transaction scope and duration",
                        "Access resources in consistent order",
                        "Use appropriate isolation levels",
                        "Consider retrying the operation"
                    }),

                // Permission Errors
                229 => (ErrorCode.PermissionDenied,
                    "Permission denied on object",
                    new List<string>
                    {
                        "Verify you have SELECT permission on the table",
                        "Contact database administrator for permission grants",
                        "Check column-level permissions if applicable"
                    }),

                // Default/Other Errors
                _ => (ErrorCode.SqlSyntaxError,
                    defaultMessage ?? $"SQL Server error {errorNumber}",
                    new List<string>
                    {
                        "Review the error message for specific details",
                        "Check SQL Server documentation for error code",
                        "Verify query syntax and object names",
                        "Contact your database administrator if needed"
                    })
            };
        }

        /// <summary>
        /// Determines if a SQL error is transient and can be retried
        /// </summary>
        private static bool IsTransientSqlError(int errorNumber)
        {
            // Transient SQL errors that might succeed on retry
            var transientErrors = new[]
            {
                -2,      // Timeout
                -1,      // Timeout
                64,      // Communication link failure
                1205,    // Deadlock
                8645,    // Timeout waiting for memory resource
                40197,   // Service error (cloud)
                40501,   // Service is busy (cloud)
                40613,   // Database unavailable (cloud)
                40540,   // Service has encountered error (cloud)
                40544,   // Query timeout (cloud)
                40549,   // Transaction timeout (cloud)
                40550,   // Long transaction (cloud)
                40551,   // Excessive log usage (cloud)
                40552,   // Excessive log space (cloud)
                40553    // Excessive memory usage (cloud)
            };

            return transientErrors.Contains(errorNumber);
        }

        /// <summary>
        /// Determines if a general exception is transient
        /// </summary>
        private static bool IsTransientException(Exception exception)
        {
            if (exception is TimeoutException)
                return true;

            if (exception is SqlException sqlEx)
                return sqlEx.Errors.Cast<SqlError>().Any(e => IsTransientSqlError(e.Number));

            return false;
        }

        /// <summary>
        /// Adds SQL error-specific troubleshooting steps
        /// </summary>
        private static void AddSqlErrorTroubleshootingSteps(ErrorContext context, int errorNumber)
        {
            // Common troubleshooting steps for all SQL errors
            context.TroubleshootingSteps.Add("Review the error message carefully");
            context.TroubleshootingSteps.Add("Check the SQL query syntax");
            context.TroubleshootingSteps.Add("Verify table and column names exist");

            // Error-specific troubleshooting
            if (errorNumber >= 100 && errorNumber < 200)
            {
                context.TroubleshootingSteps.Add("This is a syntax error - review T-SQL documentation");
            }
            else if (errorNumber >= 200 && errorNumber < 300)
            {
                context.TroubleshootingSteps.Add("This is a semantic error - check object existence");
            }
            else if (errorNumber == 1205 || errorNumber == 40001)
            {
                context.TroubleshootingSteps.Add("Consider retrying the operation");
                context.IsTransient = true;
            }

            // Add READ-ONLY mode reminder
            context.TroubleshootingSteps.Add("Remember: This MCP server is READ-ONLY and only supports SELECT queries");
        }

        /// <summary>
        /// Adds documentation links to the error context
        /// </summary>
        private static void AddDocumentationLinks(ErrorContext context, int errorNumber)
        {
            // Add SQL Server error documentation link
            context.DocumentationLinks.Add($"https://learn.microsoft.com/en-us/sql/relational-databases/errors-events/database-engine-events-and-errors");

            // Error-specific documentation
            if (errorNumber >= 100 && errorNumber < 200)
            {
                context.DocumentationLinks.Add("https://learn.microsoft.com/en-us/sql/t-sql/queries/select-transact-sql");
            }
            else if (errorNumber >= 200 && errorNumber < 300)
            {
                context.DocumentationLinks.Add("https://learn.microsoft.com/en-us/sql/relational-databases/tables/tables");
            }
        }

        /// <summary>
        /// Gets a retry recommendation for an error
        /// </summary>
        /// <param name="context">The error context</param>
        /// <returns>Retry recommendation</returns>
        public static (bool shouldRetry, int? recommendedDelayMs) GetRetryRecommendation(ErrorContext context)
        {
            if (!context.CanRetry())
                return (false, null);

            // Calculate exponential backoff delay
            var delayMs = context.Code switch
            {
                ErrorCode.ConnectionTimeout => 1000,
                ErrorCode.QueryTimeout => 2000,
                ErrorCode.DatabaseUnavailable => 5000,
                ErrorCode.ConnectionFailed => 1000,
                _ => 1000
            };

            return (true, delayMs);
        }

        /// <summary>
        /// Builds a comprehensive error response with all available information
        /// </summary>
        /// <param name="context">The error context</param>
        /// <returns>Error response object</returns>
        public static object BuildErrorResponse(ErrorContext context)
        {
            return new
            {
                code = context.Code.GetDescription(),
                message = context.Message,
                operation = context.Operation,
                can_retry = context.CanRetry(),
                is_transient = context.IsTransient,
                details = context.Details,
                sql_error = context.SqlErrorNumber.HasValue ? new
                {
                    error_number = context.SqlErrorNumber,
                    line_number = context.SqlErrorLineNumber
                } : null,
                troubleshooting_steps = context.TroubleshootingSteps,
                suggested_fixes = context.SuggestedFixes,
                documentation_links = context.DocumentationLinks,
                context = context.Context,
                inner_exception = context.InnerException
            };
        }

        /// <summary>
        /// Creates error context for blocked operations (security violations)
        /// </summary>
        /// <param name="blockedOperation">The blocked operation type</param>
        /// <param name="query">The blocked query</param>
        /// <returns>ErrorContext with security guidance</returns>
        public static ErrorContext CreateBlockedOperationContext(string blockedOperation, string query)
        {
            var context = new ErrorContext(
                ErrorCode.BlockedOperation,
                $"Operation '{blockedOperation}' is not allowed in READ-ONLY mode",
                "QueryValidation"
            );

            context.Query = query;
            context.Details["blocked_operation"] = blockedOperation;
            context.Details["security_mode"] = "READ_ONLY_ENFORCED";

            context.TroubleshootingSteps.Add("This MCP server operates in READ-ONLY mode");
            context.TroubleshootingSteps.Add("Only SELECT statements are permitted for data retrieval");
            context.TroubleshootingSteps.Add("Database listing, schema inspection, and switching are allowed operations");
            context.TroubleshootingSteps.Add("DDL (CREATE, ALTER, DROP) and DML (INSERT, UPDATE, DELETE) operations are blocked");

            context.SuggestedFixes.Add($"Remove or replace the '{blockedOperation}' operation");
            context.SuggestedFixes.Add("Use SELECT statements to query data");
            context.SuggestedFixes.Add("Use GetDatabases to list available databases");
            context.SuggestedFixes.Add("Use GetTables to inspect schema information");

            context.DocumentationLinks.Add("README.md - Tool Reference");
            context.DocumentationLinks.Add("Copilot Instructions - Security Guidelines");

            return context;
        }
    }
}
