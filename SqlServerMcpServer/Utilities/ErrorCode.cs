using System.ComponentModel;

namespace SqlServerMcpServer.Utilities
{
    /// <summary>
    /// Standardized error codes for SQL Server MCP operations
    /// </summary>
    public enum ErrorCode
    {
        // Security & Authorization Errors
        [Description("BLOCKED_OPERATION")]
        BlockedOperation,

        [Description("BLOCKED_MULTIPLE_STATEMENTS")]
        BlockedMultipleStatements,

        [Description("BLOCKED_DDL")]
        BlockedDdl,

        [Description("BLOCKED_DML")]
        BlockedDml,

        [Description("PERMISSION_DENIED")]
        PermissionDenied,

        [Description("AUTHENTICATION_FAILED")]
        AuthenticationFailed,

        // SQL Errors
        [Description("SQL_SYNTAX_ERROR")]
        SqlSyntaxError,

        [Description("INVALID_OBJECT")]
        InvalidObject,

        [Description("INVALID_COLUMN")]
        InvalidColumn,

        [Description("CONVERSION_FAILED")]
        ConversionFailed,

        [Description("ARITHMETIC_OVERFLOW")]
        ArithmeticOverflow,

        [Description("UNDEFINED_VARIABLE")]
        UndefinedVariable,

        [Description("PROCEDURE_NOT_FOUND")]
        ProcedureNotFound,

        // Connection Errors
        [Description("CONNECTION_FAILED")]
        ConnectionFailed,

        [Description("CONNECTION_TIMEOUT")]
        ConnectionTimeout,

        [Description("DATABASE_NOT_FOUND")]
        DatabaseNotFound,

        [Description("DATABASE_UNAVAILABLE")]
        DatabaseUnavailable,

        [Description("LOGIN_FAILED")]
        LoginFailed,

        [Description("CANNOT_OPEN_DATABASE")]
        CannotOpenDatabase,

        // Execution Errors
        [Description("QUERY_TIMEOUT")]
        QueryTimeout,

        [Description("EXECUTION_ERROR")]
        ExecutionError,

        [Description("RESULT_SET_ERROR")]
        ResultSetError,

        [Description("PARAMETER_BINDING_ERROR")]
        ParameterBindingError,

        // System Errors
        [Description("SYSTEM_ERROR")]
        SystemError,

        [Description("INTERNAL_ERROR")]
        InternalError,

        [Description("CONFIGURATION_ERROR")]
        ConfigurationError,

        [Description("INVALID_PARAMETER")]
        InvalidParameter,

        // Database Operation Errors
        [Description("DATABASE_SWITCH_FAILED")]
        DatabaseSwitchFailed,

        [Description("SCHEMA_READ_ERROR")]
        SchemaReadError,

        [Description("STATISTICS_ERROR")]
        StatisticsError,

        // Generic Errors
        [Description("SQL_ERROR")]
        SqlError,

        [Description("OPERATION_FAILED")]
        OperationFailed,

        [Description("UNKNOWN_ERROR")]
        UnknownError
    }

    /// <summary>
    /// Extension methods for ErrorCode enum
    /// </summary>
    public static class ErrorCodeExtensions
    {
        /// <summary>
        /// Gets the description of an error code
        /// </summary>
        public static string GetDescription(this ErrorCode code)
        {
            var field = code.GetType().GetField(code.ToString());
            if (field != null)
            {
                var attribute = System.Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) as DescriptionAttribute;
                if (attribute != null)
                {
                    return attribute.Description;
                }
            }
            return code.ToString();
        }
    }
}
