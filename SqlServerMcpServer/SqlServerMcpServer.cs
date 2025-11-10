using ModelContextProtocol.Server;
using SqlServerMcpServer.Configuration;
using SqlServerMcpServer.Operations;
using SqlServerMcpServer.Security;
using SqlServerMcpServer.Utilities;
using System.ComponentModel;

namespace SqlServerMcpServer
{
    /// <summary>
    /// Main entry point for SQL Server MCP tools - delegates to specialized operation classes
    /// </summary>
    [McpServerToolType]
    public static class SqlServerTools
    {
        /// <summary>
        /// Check connection health and server info
        /// </summary>
        /// <returns>Server health information as JSON string</returns>
        [McpServerTool, Description("Check connection health and server info")]
        public static async Task<string> GetServerHealthAsync()
        {
            return await DatabaseOperations.GetServerHealthAsync();
        }

        /// <summary>
        /// Get current database connection info
        /// </summary>
        /// <returns>Current database information as JSON string</returns>
        [McpServerTool, Description("Get current database connection info")]
        public static string GetCurrentDatabase()
        {
            return DatabaseOperations.GetCurrentDatabase();
        }

        /// <summary>
        /// Switch to a different database on the same server
        /// </summary>
        /// <param name="databaseName">The name of the database to switch to</param>
        /// <returns>Switch result as JSON string</returns>
        [McpServerTool, Description("Switch to a different database on the same server")]
        public static string SwitchDatabase([Description("The name of the database to switch to")] string databaseName)
        {
            return DatabaseOperations.SwitchDatabase(databaseName);
        }

        /// <summary>
        /// Get a list of all databases on the SQL Server instance with size and backup information
        /// </summary>
        /// <param name="includeSystemDatabases">Include system databases (optional, default: false)</param>
        /// <param name="minSizeMB">Filter by minimum size in MB (optional)</param>
        /// <param name="stateFilter">Filter by database state: 'ONLINE', 'OFFLINE', or 'ALL' (default: 'ONLINE')</param>
        /// <param name="nameFilter">Filter by database name (partial match, optional)</param>
        /// <returns>Database list as JSON string</returns>
        [McpServerTool, Description("Get a list of all databases on the SQL Server instance with size and backup information")]
        public static async Task<string> GetDatabasesAsync(
            [Description("Include system databases (optional, default: false)")] bool includeSystemDatabases = false,
            [Description("Filter by minimum size in MB (optional)")] decimal? minSizeMB = null,
            [Description("Filter by database state: 'ONLINE', 'OFFLINE', or 'ALL' (default: 'ONLINE')")] string? stateFilter = "ONLINE",
            [Description("Filter by database name (partial match, optional)")] string? nameFilter = null)
        {
            return await DatabaseOperations.GetDatabasesAsync(includeSystemDatabases, minSizeMB, stateFilter, nameFilter);
        }

        /// <summary>
        /// Execute a read-only SQL query on the current database with pagination and metadata
        /// </summary>
        /// <param name="query">The SQL query to execute (SELECT statements only)</param>
        /// <param name="maxRows">Maximum rows to return (default 100, max 1000)</param>
        /// <param name="offset">Offset for pagination (default: 0)</param>
        /// <param name="pageSize">Page size for pagination (default: 100, max: 1000)</param>
        /// <param name="includeStatistics">Include query execution statistics (optional, default: false)</param>
        /// <returns>Query results as JSON string</returns>
        [McpServerTool, Description("Execute a read-only SQL query on the current database with pagination and metadata")]
        public static async Task<string> ExecuteQueryAsync(
            [Description("The SQL query to execute (SELECT statements only)")] string query,
            [Description("Maximum rows to return (default 100, max 1000)")] int? maxRows = 100,
            [Description("Offset for pagination (default: 0)")] int? offset = 0,
            [Description("Page size for pagination (default: 100, max: 1000)")] int? pageSize = 100,
            [Description("Include query execution statistics (optional, default: false)")] bool includeStatistics = false)
        {
            return await QueryExecution.ExecuteQueryAsync(query, maxRows, offset, pageSize, includeStatistics);
        }

        /// <summary>
        /// Execute a read-only SQL query with formatting and parameters (SRS: read_query)
        /// </summary>
        /// <param name="query">T-SQL SELECT statement (read-only)</param>
        /// <param name="timeout">Per-call timeout in seconds (default 30, range 1–300)</param>
        /// <param name="max_rows">Maximum rows to return (default 1000, range 1–10000)</param>
        /// <param name="format">Result format: json | csv | table (HTML)</param>
        /// <param name="parameters">Named parameters to bind (e.g., { id: 42 })</param>
        /// <param name="delimiter">CSV delimiter (default ','. Use 'tab' or \t for tab)</param>
        /// <returns>Query results as JSON string</returns>
        [McpServerTool, Description("Execute a read-only SQL query with formatting and parameters (SRS: read_query)")]
        public static async Task<string> ReadQueryAsync(
            [Description("T-SQL SELECT statement (read-only)")] string query,
            [Description("Per-call timeout in seconds (default 30, range 1–300)")] int? timeout = null,
            [Description("Maximum rows to return (default 1000, range 1–10000)")] int? max_rows = 1000,
            [Description("Result format: json | csv | table (HTML)")] string? format = "json",
            [Description("Named parameters to bind (e.g., { id: 42 })")] Dictionary<string, object>? parameters = null,
            [Description("CSV delimiter (default ','. Use 'tab' or \\t for tab)")] string? delimiter = null)
        {
            return await QueryExecution.ReadQueryAsync(query, timeout, max_rows, format, parameters, delimiter);
        }

        /// <summary>
        /// Get a list of all tables in the current database with size information and filtering options
        /// </summary>
        /// <param name="schemaFilter">Filter tables by schema name (optional)</param>
        /// <param name="nameFilter">Filter by table name (partial match, optional)</param>
        /// <param name="minRowCount">Minimum row count filter (optional)</param>
        /// <param name="sortBy">Sort by: 'NAME', 'SIZE', or 'ROWS' (default: 'NAME')</param>
        /// <param name="sortOrder">Sort order: 'ASC' or 'DESC' (default: 'ASC')</param>
        /// <returns>Table list as JSON string</returns>
        [McpServerTool, Description("Get a list of all tables in the current database with size information and filtering options")]
        public static async Task<string> GetTablesAsync(
            [Description("Filter tables by schema name (optional)")] string? schemaFilter = null,
            [Description("Filter by table name (partial match, optional)")] string? nameFilter = null,
            [Description("Minimum row count filter (optional)")] int? minRowCount = null,
            [Description("Sort by: 'NAME', 'SIZE', or 'ROWS' (default: 'NAME')")] string? sortBy = "NAME",
            [Description("Sort order: 'ASC' or 'DESC' (default: 'ASC')")] string? sortOrder = "ASC")
        {
            return await SchemaInspection.GetTablesAsync(schemaFilter, nameFilter, minRowCount, sortBy, sortOrder);
        }

        /// <summary>
        /// Get the schema information for a specific table with PK/FK info, indexes, and extended properties
        /// </summary>
        /// <param name="tableName">Name of the table</param>
        /// <param name="schemaName">Schema name (defaults to 'dbo')</param>
        /// <param name="includeStatistics">Include column statistics (optional, default: false)</param>
        /// <returns>Table schema as JSON string</returns>
        [McpServerTool, Description("Get the schema information for a specific table with PK/FK info, indexes, and extended properties")]
        public static async Task<string> GetTableSchemaAsync(
            [Description("Name of the table")] string tableName,
            [Description("Schema name (defaults to 'dbo')")] string? schemaName = "dbo",
            [Description("Include column statistics (optional, default: false)")] bool includeStatistics = false)
        {
            return await SchemaInspection.GetTableSchemaAsync(tableName, schemaName, includeStatistics);
        }

        /// <summary>
        /// Get a list of stored procedures in the current database
        /// </summary>
        /// <param name="nameFilter">Filter by procedure name (partial match, optional)</param>
        /// <returns>Stored procedure list as JSON string</returns>
        [McpServerTool, Description("Get a list of stored procedures in the current database")]
        public static async Task<string> GetStoredProceduresAsync(
            [Description("Filter by procedure name (partial match, optional)")] string? nameFilter = null)
        {
            return await SchemaInspection.GetStoredProceduresAsync(nameFilter);
        }

        /// <summary>
        /// Get detailed information about a specific stored procedure including parameters and definition
        /// </summary>
        /// <param name="procedureName">Name of the stored procedure</param>
        /// <param name="schemaName">Schema name (defaults to 'dbo')</param>
        /// <returns>Stored procedure details as JSON string</returns>
        [McpServerTool, Description("Get detailed information about a specific stored procedure including parameters and definition")]
        public static async Task<string> GetStoredProcedureDetailsAsync(
            [Description("Name of the stored procedure")] string procedureName,
            [Description("Schema name (defaults to 'dbo')")] string? schemaName = "dbo")
        {
            return await SchemaInspection.GetStoredProcedureDetailsAsync(procedureName, schemaName);
        }

        /// <summary>
        /// Get detailed information about any database object (stored procedure, function, or view) including definition, parameters, and dependencies
        /// </summary>
        /// <param name="objectName">Name of the database object (procedure, function, or view)</param>
        /// <param name="schemaName">Schema name (defaults to 'dbo')</param>
        /// <param name="objectType">Object type: 'PROCEDURE', 'FUNCTION', 'VIEW', or 'AUTO' to auto-detect (default)</param>
        /// <returns>Object definition as JSON string</returns>
        [McpServerTool, Description("Get detailed information about any database object (stored procedure, function, or view) including definition, parameters, and dependencies")]
        public static async Task<string> GetObjectDefinitionAsync(
            [Description("Name of the database object (procedure, function, or view)")] string objectName,
            [Description("Schema name (defaults to 'dbo')")] string? schemaName = "dbo",
            [Description("Object type: 'PROCEDURE', 'FUNCTION', 'VIEW', or 'AUTO' to auto-detect (default)")] string? objectType = "AUTO")
        {
            return await SchemaInspection.GetObjectDefinitionAsync(objectName, schemaName, objectType);
        }
    }
}
