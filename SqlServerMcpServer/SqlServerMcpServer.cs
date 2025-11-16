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

        [McpServerTool, Description("Get SQL Server wait statistics")]
        public static async Task<string> GetWaitStatsAsync(
            [Description("Maximum number of wait types to return (default: 20)")] int topN = 20,
            [Description("Include system wait types (default: false)")] bool includeSystemWaits = false,
            [Description("Reset wait stats after reading (default: false)")] bool resetStats = false)
        {
            return await PerformanceAnalysis.GetWaitStats(topN, includeSystemWaits, resetStats);
        }

        [McpServerTool, Description("Find columns by data type across all tables")]
        public static async Task<string> FindColumnsByDataTypeAsync(
            [Description("SQL Server data type to search for (e.g., 'int', 'varchar', 'datetime')")] string dataType,
            [Description("Schema name filter (optional)")] string? schemaName = null,
            [Description("Table name filter (optional)")] string? tableName = null,
            [Description("Include nullable columns (default: true)")] bool includeNullable = true,
            [Description("Include non-nullable columns (default: true)")] bool includeNotNullable = true,
            [Description("Include identity columns (default: true)")] bool includeIdentity = true,
            [Description("Include computed columns (default: false)")] bool includeComputed = false)
        {
            return await DataDiscovery.FindColumnsByDataType(dataType, schemaName, tableName, includeNullable, includeNotNullable, includeIdentity, includeComputed);
        }

        // ==================== Diagnostics Operations ====================

        [McpServerTool, Description("Get database size summary with optional per-table breakdown")]
        public static async Task<string> GetDatabaseSizeAsync(
            [Description("Include table-level size breakdown (default: true)")] bool includeTableBreakdown = true,
            [Description("Maximum number of tables to include (default: 20)")] int topN = 20)
        {
            return await Diagnostics.GetDatabaseSize(includeTableBreakdown, topN);
        }

        [McpServerTool, Description("Get backup history from msdb for the current or specified database")]
        public static async Task<string> GetBackupHistoryAsync(
            [Description("Database name filter (optional - defaults to current database)")] string? databaseName = null,
            [Description("Maximum number of records to return (default: 20)")] int topN = 20)
        {
            return await Diagnostics.GetBackupHistory(databaseName, topN);
        }

        [McpServerTool, Description("Get recent diagnostic events from the system_health Extended Events session")]
        public static async Task<string> GetErrorLogAsync(
            [Description("Maximum number of events to return (default: 100)")] int topN = 100)
        {
            return await Diagnostics.GetErrorLog(topN);
        }

        // ==================== Schema Analysis Operations ====================

        [McpServerTool, Description("Discover foreign key relationships between tables")]
        public static async Task<string> GetTableRelationshipsAsync(
            [Description("Table name to filter relationships (optional)")] string? tableName = null,
            [Description("Schema name (default: 'dbo')")] string? schemaName = "dbo",
            [Description("Include tables that reference this table (default: true)")] bool includeReferencedBy = true,
            [Description("Include tables this table references (default: true)")] bool includeReferences = true)
        {
            return await SchemaAnalysis.GetTableRelationships(tableName, schemaName, includeReferencedBy, includeReferences);
        }

        [McpServerTool, Description("List indexes with usage statistics")]
        public static async Task<string> GetIndexInformationAsync(
            [Description("Table name to filter indexes (optional)")] string? tableName = null,
            [Description("Schema name (default: 'dbo')")] string? schemaName = "dbo",
            [Description("Include usage statistics (default: true)")] bool includeStatistics = true)
        {
            return await SchemaAnalysis.GetIndexInformation(tableName, schemaName, includeStatistics);
        }

        // ==================== Performance Analysis Operations ====================

        [McpServerTool, Description("Get SQL Server's missing index suggestions")]
        public static async Task<string> GetMissingIndexesAsync(
            [Description("Table name to filter suggestions (optional)")] string? tableName = null,
            [Description("Minimum impact score threshold (default: 10.0)")] decimal minImpact = 10.0m,
            [Description("Maximum number of suggestions to return (default: 20)")] int topN = 20)
        {
            return await PerformanceAnalysis.GetMissingIndexes(tableName, minImpact, topN);
        }

        [McpServerTool, Description("Retrieve execution plan without executing query")]
        public static async Task<string> GetQueryExecutionPlanAsync(
            [Description("SQL query to analyze (required)")] string query,
            [Description("Plan type: 'ESTIMATED' or 'SHOWPLAN_XML' (default: 'ESTIMATED')")] string planType = "ESTIMATED",
            [Description("Include performance analysis (default: true)")] bool includeAnalysis = true)
        {
            return await PerformanceAnalysis.GetQueryExecutionPlan(query, planType, includeAnalysis);
        }

        [McpServerTool, Description("Analyze index fragmentation levels")]
        public static async Task<string> GetIndexFragmentationAsync(
            [Description("Table name to filter (optional)")] string? tableName = null,
            [Description("Schema name (default: 'dbo')")] string? schemaName = "dbo",
            [Description("Minimum fragmentation percentage (default: 10.0)")] decimal minFragmentation = 10.0m,
            [Description("Include online rebuild eligibility (default: true)")] bool includeOnlineStatus = true)
        {
            return await PerformanceAnalysis.GetIndexFragmentation(tableName, schemaName, minFragmentation, includeOnlineStatus);
        }

        // ==================== Data Discovery Operations ====================

        [McpServerTool, Description("Search for data across tables with pattern matching")]
        public static async Task<string> SearchTableDataAsync(
            [Description("Search pattern (supports LIKE wildcards)")] string searchPattern,
            [Description("Table name to search (optional)")] string? tableName = null,
            [Description("Schema name (default: 'dbo')")] string? schemaName = "dbo",
            [Description("Specific column names to search (optional, comma-separated)")] string? columnNames = null,
            [Description("Maximum rows to return per table (default: 10)")] int maxRows = 10,
            [Description("Maximum tables to search (default: 10)")] int maxTables = 10)
        {
            return await DataDiscovery.SearchTableData(searchPattern, tableName, schemaName, columnNames, maxRows, maxTables);
        }

        [McpServerTool, Description("Get column statistics and data distribution")]
        public static async Task<string> GetColumnStatisticsAsync(
            [Description("Table name (required)")] string tableName,
            [Description("Schema name (default: 'dbo')")] string? schemaName = "dbo",
            [Description("Column name (optional - if not provided, returns all columns)")] string? columnName = null,
            [Description("Include histogram data (default: false)")] bool includeHistogram = false,
            [Description("Sample size for statistics (default: 10000)")] int sampleSize = 10000)
        {
            return await DataDiscovery.GetColumnStatistics(tableName, schemaName, columnName, includeHistogram, sampleSize);
        }

        [McpServerTool, Description("Find tables containing specific column names")]
        public static async Task<string> FindTablesWithColumnAsync(
            [Description("Column name to search for (supports wildcards)")] string columnName,
            [Description("Schema name filter (optional)")] string? schemaName = null,
            [Description("Exact column name match (default: false)")] bool exactMatch = false,
            [Description("Include system tables (default: false)")] bool includeSystemTables = false,
            [Description("Include row count for each table (default: true)")] bool includeRowCount = true,
            [Description("Maximum number of results (default: 100)")] int maxResults = 100)
        {
            return await DataDiscovery.FindTablesWithColumn(columnName, schemaName, exactMatch, includeSystemTables, includeRowCount, maxResults);
        }

        // ==================== Code Generation Operations ====================

        [McpServerTool, Description("Generate C# model class code based on table schema for .NET applications")]
        public static async Task<string> GenerateModelClassAsync(
            [Description("Table name (required)")] string tableName,
            [Description("Schema name (default: 'dbo')")] string? schemaName = "dbo",
            [Description("Custom class name (optional - defaults to table name)")] string? className = null,
            [Description("Namespace for C# (default: 'GeneratedModels')")] string @namespace = "GeneratedModels",
            [Description("Include DataAnnotations validation attributes (default: true)")] bool includeValidation = true,
            [Description("Include Table/Column attributes and XML documentation (default: true)")] bool includeAnnotations = true)
        {
            return await CodeGeneration.GenerateModelClass(tableName, schemaName, className, @namespace, includeValidation, includeAnnotations);
        }

        // ==================== Performance & Reliability Diagnostics ====================

        [McpServerTool, Description("Get cache metrics and performance statistics")]
        public static string GetCacheMetrics()
        {
            try
            {
                var cacheService = new CacheService();
                var metrics = cacheService.GetMetrics();
                var cacheInfo = cacheService.GetCacheInfo();

                var payload = new
                {
                    server_name = SqlConnectionManager.ServerName,
                    environment = SqlConnectionManager.Environment,
                    database = SqlConnectionManager.CurrentDatabase,
                    cache_metrics = new
                    {
                        hits = metrics.Hits,
                        misses = metrics.Misses,
                        total_operations = metrics.TotalOperations,
                        hit_ratio_percent = Math.Round(metrics.HitRatio, 2),
                        timestamp_utc = metrics.Timestamp
                    },
                    cache_configuration = new
                    {
                        enabled = cacheInfo.Enabled,
                        default_ttl_seconds = cacheInfo.DefaultTTLSeconds,
                        schema_ttl_seconds = cacheInfo.SchemaTTLSeconds,
                        procedure_ttl_seconds = cacheInfo.ProcedureTTLSeconds,
                        current_entries_count = cacheInfo.CurrentEntriesCount,
                        cached_key_prefixes = cacheInfo.CachedKeyPrefixes
                    },
                    operation_type = "DIAGNOSTICS",
                    security_mode = "READ_ONLY_ENFORCED"
                };

                return ResponseFormatter.ToJson(payload);
            }
            catch (Exception ex)
            {
                var context = ErrorHelper.CreateErrorContextFromException(ex, "GetCacheMetrics");
                var response = ResponseFormatter.CreateErrorContextResponse(context, 0);
                return ResponseFormatter.ToJson(response);
            }
        }

        [McpServerTool, Description("Get connection pool statistics and resilience metrics")]
        public static string GetConnectionPoolStats()
        {
            try
            {
                var poolStats = ConnectionPoolManager.GetPoolStatistics();

                var payload = new
                {
                    server_name = SqlConnectionManager.ServerName,
                    environment = SqlConnectionManager.Environment,
                    database = SqlConnectionManager.CurrentDatabase,
                    connection_pool_statistics = new
                    {
                        total_attempts = poolStats.TotalAttempts,
                        successful_connections = poolStats.SuccessfulConnections,
                        failed_connections = poolStats.FailedConnections,
                        retried_connections = poolStats.RetriedConnections,
                        success_rate_percent = Math.Round(poolStats.SuccessRate, 2),
                        retry_rate_percent = Math.Round(poolStats.RetryRate, 2)
                    },
                    connection_resilience_settings = new
                    {
                        retry_enabled = true,
                        circuit_breaker_enabled = true,
                        max_retry_attempts = 3,
                        initial_retry_delay_ms = 100,
                        max_retry_delay_ms = 5000,
                        circuit_breaker_threshold = 5,
                        circuit_breaker_break_duration_seconds = 30
                    },
                    operation_type = "DIAGNOSTICS",
                    security_mode = "READ_ONLY_ENFORCED"
                };

                return ResponseFormatter.ToJson(payload);
            }
            catch (Exception ex)
            {
                var context = ErrorHelper.CreateErrorContextFromException(ex, "GetConnectionPoolStats");
                var response = ResponseFormatter.CreateErrorContextResponse(context, 0);
                return ResponseFormatter.ToJson(response);
            }
        }
    }
}
