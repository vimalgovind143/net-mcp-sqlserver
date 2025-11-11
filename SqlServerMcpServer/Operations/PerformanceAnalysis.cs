using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;
using SqlServerMcpServer.Configuration;
using SqlServerMcpServer.Security;
using SqlServerMcpServer.Utilities;
using System.ComponentModel;

namespace SqlServerMcpServer.Operations
{
    /// <summary>
    /// Handles performance analysis operations for indexes, queries, and server statistics
    /// </summary>
    public static class PerformanceAnalysis
    {
        /// <summary>
        /// Get SQL Server's missing index suggestions
        /// </summary>
        /// <param name="tableName">Table name to filter suggestions (optional)</param>
        /// <param name="minImpact">Minimum impact score threshold (default: 10.0)</param>
        /// <param name="topN">Maximum number of suggestions to return (default: 20)</param>
        /// <returns>Missing index suggestions as JSON string</returns>
        [McpServerTool, Description("Get SQL Server's missing index suggestions")]
        public static async Task<string> GetMissingIndexes(
            [Description("Table name to filter suggestions (optional)")] string? tableName = null,
            [Description("Minimum impact score threshold (default: 10.0)")] decimal minImpact = 10.0m,
            [Description("Maximum number of suggestions to return (default: 20)")] int topN = 20)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("GetMissingIndexes", $"table:{tableName}, minImpact:{minImpact}, topN:{topN}");
                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

                var query = @"
                    SELECT TOP (@TopN)
                        DB_NAME() AS database_name,
                        OBJECT_NAME(mid.object_id) AS table_name,
                        mid.equality_columns,
                        mid.inequality_columns,
                        mid.included_columns,
                        migs.user_seeks,
                        migs.user_scans,
                        migs.avg_total_user_cost,
                        migs.avg_user_impact,
                        migs.user_seeks * migs.avg_total_user_cost * (migs.avg_user_impact / 100.0) AS impact_score,
                        'CREATE INDEX [IX_' + OBJECT_NAME(mid.object_id) + '_' + 
                        REPLACE(ISNULL(mid.equality_columns, '') + 
                               CASE WHEN mid.equality_columns IS NOT NULL AND mid.inequality_columns IS NOT NULL THEN '_' ELSE '' END +
                               ISNULL(mid.inequality_columns, ''), ',', '_') + 
                        CASE WHEN mid.included_columns IS NOT NULL THEN '_INC' ELSE '' END + 
                        '] ON ' + OBJECT_NAME(mid.object_id) + ' (' +
                        ISNULL(mid.equality_columns, '') +
                        CASE WHEN mid.equality_columns IS NOT NULL AND mid.inequality_columns IS NOT NULL THEN ', ' ELSE '' END +
                        ISNULL(mid.inequality_columns, '') + ')' +
                        CASE WHEN mid.included_columns IS NOT NULL THEN ' INCLUDE (' + mid.included_columns + ')' ELSE '' END AS create_index_statement
                    FROM sys.dm_db_missing_index_details mid
                    INNER JOIN sys.dm_db_missing_index_groups mig ON mid.index_handle = mig.index_handle
                    INNER JOIN sys.dm_db_missing_index_group_stats migs ON mig.index_group_handle = migs.group_handle
                    WHERE (@TableName IS NULL OR OBJECT_NAME(mid.object_id) = @TableName)
                    AND migs.avg_user_impact >= @MinImpact
                    ORDER BY impact_score DESC";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@TableName", (object?)tableName ?? DBNull.Value);
                command.Parameters.AddWithValue("@MinImpact", minImpact);
                command.Parameters.AddWithValue("@TopN", topN);

                using var reader = await command.ExecuteReaderAsync();
                
                var missingIndexes = new List<Dictionary<string, object>>();
                while (await reader.ReadAsync())
                {
                    var index = new Dictionary<string, object>
                    {
                        ["table_name"] = reader["table_name"].ToString()!,
                        ["equality_columns"] = reader["equality_columns"] != DBNull.Value 
                            ? reader["equality_columns"].ToString()!.Split(", ", StringSplitOptions.RemoveEmptyEntries).ToList()
                            : new List<string>(),
                        ["inequality_columns"] = reader["inequality_columns"] != DBNull.Value 
                            ? reader["inequality_columns"].ToString()!.Split(", ", StringSplitOptions.RemoveEmptyEntries).ToList()
                            : new List<string>(),
                        ["included_columns"] = reader["included_columns"] != DBNull.Value 
                            ? reader["included_columns"].ToString()!.Split(", ", StringSplitOptions.RemoveEmptyEntries).ToList()
                            : new List<string>(),
                        ["user_seeks"] = Convert.ToInt64(reader["user_seeks"]),
                        ["user_scans"] = Convert.ToInt64(reader["user_scans"]),
                        ["avg_total_user_cost"] = Convert.ToDecimal(reader["avg_total_user_cost"]),
                        ["avg_user_impact"] = Convert.ToDecimal(reader["avg_user_impact"]),
                        ["impact_score"] = Convert.ToDecimal(reader["impact_score"]),
                        ["create_index_statement"] = reader["create_index_statement"].ToString()!
                    };
                    missingIndexes.Add(index);
                }

                var payload = new
                {
                    server_name = SqlConnectionManager.ServerName,
                    environment = SqlConnectionManager.Environment,
                    database = SqlConnectionManager.CurrentDatabase,
                    suggestion_count = missingIndexes.Count,
                    filters_applied = new
                    {
                        table_name = tableName,
                        min_impact = minImpact,
                        top_n = topN
                    },
                    missing_indexes = missingIndexes
                };
                sw.Stop();
                LoggingHelper.LogEnd(corr, "GetMissingIndexes", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "GetMissingIndexes");
                LoggingHelper.LogEnd(Guid.Empty, "GetMissingIndexes", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "GetMissingIndexes");
                LoggingHelper.LogEnd(Guid.Empty, "GetMissingIndexes", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }

        /// <summary>
        /// Retrieve execution plan without executing query
        /// </summary>
        /// <param name="query">SQL query to analyze (required)</param>
        /// <param name="planType">Plan type: 'ESTIMATED' or 'SHOWPLAN_XML' (default: 'ESTIMATED')</param>
        /// <param name="includeAnalysis">Include performance analysis (default: true)</param>
        /// <returns>Execution plan as JSON string</returns>
        [McpServerTool, Description("Retrieve execution plan without executing query")]
        public static async Task<string> GetQueryExecutionPlan(
            [Description("SQL query to analyze (required)")] string query,
            [Description("Plan type: 'ESTIMATED' or 'SHOWPLAN_XML' (default: 'ESTIMATED')")] string planType = "ESTIMATED",
            [Description("Include performance analysis (default: true)")] bool includeAnalysis = true)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("GetQueryExecutionPlan", $"planType:{planType}");
                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

                // Validate query is read-only
                if (!QueryValidator.IsReadOnlyQuery(query, out string blockedOperation))
                {
                    throw new ArgumentException($"Only read-only queries are allowed for execution plan analysis. Blocked operation: {blockedOperation}");
                }

                planType = planType.ToUpperInvariant() switch
                {
                    "SHOWPLAN_XML" => "SHOWPLAN_XML",
                    "ESTIMATED" => "ESTIMATED",
                    _ => "ESTIMATED"
                };

                var setOptions = planType == "SHOWPLAN_XML" 
                    ? "SET SHOWPLAN_XML ON;" 
                    : "SET SHOWPLAN_ALL ON;";

                var actualQuery = $"{setOptions}\n{query}";
                
                using var command = new SqlCommand(actualQuery, connection);
                command.CommandTimeout = 30;

                string planXml = "";
                decimal estimatedCost = 0;
                long estimatedRows = 0;

                try
                {
                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        if (planType == "SHOWPLAN_XML")
                        {
                            planXml = reader[0].ToString()!;
                        }
                        else
                        {
                            // For SHOWPLAN_ALL, we need to parse the textual output
                            // This is a simplified version - in practice you'd want more sophisticated parsing
                            planXml = reader.GetString(0); // This contains the plan as text
                        }
                    }
                }
                finally
                {
                    // Reset the SHOWPLAN setting
                    using var resetCommand = new SqlCommand("SET SHOWPLAN_XML OFF; SET SHOWPLAN_ALL OFF;", connection);
                    await resetCommand.ExecuteNonQueryAsync();
                }

                var analysis = new Dictionary<string, object>();
                if (includeAnalysis && !string.IsNullOrEmpty(planXml))
                {
                    var warnings = new List<string>();
                    var recommendations = new List<string>();
                    var expensiveOperators = new List<Dictionary<string, object>>();

                    // Simple XML parsing for common performance issues
                    if (planXml.Contains("Table Scan"))
                    {
                        warnings.Add("Table Scan detected - consider adding appropriate indexes");
                        recommendations.Add("Review indexes for tables showing Table Scan operations");
                    }

                    if (planXml.Contains("Key Lookup"))
                    {
                        warnings.Add("Key Lookup operations detected");
                        recommendations.Add("Consider including additional columns in indexes to avoid Key Lookups");
                    }

                    if (planXml.Contains("Sort") && planXml.Contains("Table Scan"))
                    {
                        warnings.Add("Sort operation without proper indexing");
                        recommendations.Add("Add indexes that support the ORDER BY clause");
                    }

                    // Extract estimated cost and rows from XML (simplified)
                    var costMatch = System.Text.RegularExpressions.Regex.Match(planXml, @"EstimatedTotalSubtreeCost=""([^""]+)""");
                    if (costMatch.Success && decimal.TryParse(costMatch.Groups[1].Value, out var cost))
                    {
                        estimatedCost = cost;
                    }

                    var rowsMatch = System.Text.RegularExpressions.Regex.Match(planXml, @"EstimatedRows=""([^""]+)""");
                    if (rowsMatch.Success && long.TryParse(rowsMatch.Groups[1].Value, out var rows))
                    {
                        estimatedRows = rows;
                    }

                    analysis["warnings"] = warnings;
                    analysis["recommendations"] = recommendations;
                    analysis["expensive_operators"] = expensiveOperators;
                }

                var payload = new
                {
                    server_name = SqlConnectionManager.ServerName,
                    environment = SqlConnectionManager.Environment,
                    database = SqlConnectionManager.CurrentDatabase,
                    execution_plan = new
                    {
                        plan_xml = planXml,
                        estimated_cost = estimatedCost,
                        estimated_rows = estimatedRows,
                        plan_type = planType.ToLowerInvariant()
                    },
                    analysis = analysis,
                    parameters = new
                    {
                        plan_type = planType,
                        include_analysis = includeAnalysis
                    }
                };
                sw.Stop();
                LoggingHelper.LogEnd(corr, "GetQueryExecutionPlan", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "GetQueryExecutionPlan");
                LoggingHelper.LogEnd(Guid.Empty, "GetQueryExecutionPlan", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "GetQueryExecutionPlan");
                LoggingHelper.LogEnd(Guid.Empty, "GetQueryExecutionPlan", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }

        /// <summary>
        /// Analyze index fragmentation levels
        /// </summary>
        /// <param name="tableName">Table name to filter indexes (optional)</param>
        /// <param name="schemaName">Schema name (default: 'dbo')</param>
        /// <param name="minFragmentation">Minimum fragmentation percentage (default: 5.0)</param>
        /// <param name="includeOnlineStatus">Include online/offline status (default: true)</param>
        /// <returns>Index fragmentation analysis as JSON string</returns>
        [McpServerTool, Description("Analyze index fragmentation levels")]
        public static async Task<string> GetIndexFragmentation(
            [Description("Table name to filter indexes (optional)")] string? tableName = null,
            [Description("Schema name (default: 'dbo')")] string? schemaName = "dbo",
            [Description("Minimum fragmentation percentage (default: 5.0)")] decimal minFragmentation = 5.0m,
            [Description("Include online/offline status (default: true)")] bool includeOnlineStatus = true)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("GetIndexFragmentation", $"table:{tableName}, schema:{schemaName}, minFrag:{minFragmentation}");
                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

                var query = @"
                    SELECT 
                        OBJECT_NAME(ind.OBJECT_ID) AS table_name,
                        SCHEMA_NAME(t.schema_id) AS schema_name,
                        ind.name AS index_name,
                        ind.type_desc AS index_type,
                        ind.is_primary_key,
                        ind.is_unique,
                        ind.is_disabled,
                        ind.is_hypothetical,
                        ind.fill_factor,
                        ps.avg_fragmentation_in_percent,
                        ps.fragment_count,
                        ps.page_count,
                        CONVERT(DECIMAL(15,2), (ps.page_count * 8.0) / 1024.0) AS size_mb,
                        ps.avg_page_space_used_in_percent,
                        ps.record_count,
                        ps.ghost_record_count,
                        ps.version_ghost_record_count
                    FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ps
                    INNER JOIN sys.indexes ind ON ps.object_id = ind.object_id AND ps.index_id = ind.index_id
                    INNER JOIN sys.tables t ON ind.object_id = t.object_id
                    WHERE (@TableName IS NULL OR OBJECT_NAME(ind.object_id) = @TableName)
                    AND (@SchemaName IS NULL OR SCHEMA_NAME(t.schema_id) = @SchemaName)
                    AND ps.avg_fragmentation_in_percent >= @MinFragmentation
                    AND ind.name IS NOT NULL
                    ORDER BY ps.avg_fragmentation_in_percent DESC, table_name, index_name";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@TableName", (object?)tableName ?? DBNull.Value);
                command.Parameters.AddWithValue("@SchemaName", (object?)schemaName ?? DBNull.Value);
                command.Parameters.AddWithValue("@MinFragmentation", minFragmentation);

                using var reader = await command.ExecuteReaderAsync();
                
                var fragmentedIndexes = new List<Dictionary<string, object>>();
                var summary = new Dictionary<string, object>
                {
                    ["total_fragmented_indexes"] = 0,
                    ["high_fragmentation_count"] = 0,
                    ["medium_fragmentation_count"] = 0,
                    ["low_fragmentation_count"] = 0,
                    ["total_size_mb"] = 0m
                };

                while (await reader.ReadAsync())
                {
                    var fragmentation = Convert.ToDecimal(reader["avg_fragmentation_in_percent"]);
                    var sizeMb = Convert.ToDecimal(reader["size_mb"]);
                    
                    var index = new Dictionary<string, object>
                    {
                        ["table_name"] = reader["table_name"].ToString()!,
                        ["schema_name"] = reader["schema_name"].ToString()!,
                        ["index_name"] = reader["index_name"].ToString()!,
                        ["index_type"] = reader["index_type"].ToString()!,
                        ["is_primary_key"] = Convert.ToBoolean(reader["is_primary_key"]),
                        ["is_unique"] = Convert.ToBoolean(reader["is_unique"]),
                        ["is_disabled"] = Convert.ToBoolean(reader["is_disabled"]),
                        ["fill_factor"] = Convert.ToInt32(reader["fill_factor"]),
                        ["avg_fragmentation_in_percent"] = fragmentation,
                        ["fragment_count"] = Convert.ToInt64(reader["fragment_count"]),
                        ["page_count"] = Convert.ToInt64(reader["page_count"]),
                        ["size_mb"] = sizeMb,
                        ["avg_page_space_used_in_percent"] = Convert.ToDecimal(reader["avg_page_space_used_in_percent"]),
                        ["record_count"] = Convert.ToInt64(reader["record_count"]),
                        ["ghost_record_count"] = Convert.ToInt64(reader["ghost_record_count"]),
                        ["fragmentation_level"] = fragmentation >= 30.0m ? "HIGH" : fragmentation >= 10.0m ? "MEDIUM" : "LOW",
                        ["recommendation"] = fragmentation >= 30.0m ? "REBUILD" : fragmentation >= 10.0m ? "REORGANIZE" : "MONITOR"
                    };

                    if (includeOnlineStatus)
                    {
                        index["can_rebuild_online"] = reader["index_type"].ToString() != "HEAP" && 
                                                      reader["index_type"].ToString() != "XML";
                    }

                    fragmentedIndexes.Add(index);

                    // Update summary
                    summary["total_fragmented_indexes"] = Convert.ToInt32(summary["total_fragmented_indexes"]) + 1;
                    summary["total_size_mb"] = Convert.ToDecimal(summary["total_size_mb"]) + sizeMb;

                    if (fragmentation >= 30.0m)
                        summary["high_fragmentation_count"] = Convert.ToInt32(summary["high_fragmentation_count"]) + 1;
                    else if (fragmentation >= 10.0m)
                        summary["medium_fragmentation_count"] = Convert.ToInt32(summary["medium_fragmentation_count"]) + 1;
                    else
                        summary["low_fragmentation_count"] = Convert.ToInt32(summary["low_fragmentation_count"]) + 1;
                }

                var payload = new
                {
                    server_name = SqlConnectionManager.ServerName,
                    environment = SqlConnectionManager.Environment,
                    database = SqlConnectionManager.CurrentDatabase,
                    fragmented_indexes = fragmentedIndexes,
                    summary = summary,
                    filters_applied = new
                    {
                        table_name = tableName,
                        schema_name = schemaName,
                        min_fragmentation = minFragmentation,
                        include_online_status = includeOnlineStatus
                    }
                };
                sw.Stop();
                LoggingHelper.LogEnd(corr, "GetIndexFragmentation", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "GetIndexFragmentation");
                LoggingHelper.LogEnd(Guid.Empty, "GetIndexFragmentation", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "GetIndexFragmentation");
                LoggingHelper.LogEnd(Guid.Empty, "GetIndexFragmentation", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }

        /// <summary>
        /// Get SQL Server wait statistics
        /// </summary>
        /// <param name="topN">Maximum number of wait types to return (default: 20)</param>
        /// <param name="includeSystemWaits">Include system wait types (default: false)</param>
        /// <param name="resetStats">Reset wait stats after reading (default: false)</param>
        /// <returns>Wait statistics as JSON string</returns>
        [McpServerTool, Description("Get SQL Server wait statistics")]
        public static async Task<string> GetWaitStats(
            [Description("Maximum number of wait types to return (default: 20)")] int topN = 20,
            [Description("Include system wait types (default: false)")] bool includeSystemWaits = false,
            [Description("Reset wait stats after reading (default: false)")] bool resetStats = false)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("GetWaitStats", $"topN:{topN}, includeSystem:{includeSystemWaits}, reset:{resetStats}");
                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

                var query = @"
                    SELECT TOP (@TopN)
                        ws.wait_type,
                        ws.waiting_tasks_count,
                        ws.wait_time_ms,
                        CONVERT(DECIMAL(15,2), ws.wait_time_ms * 1.0 / NULLIF(ws.waiting_tasks_count, 0)) AS avg_wait_time_ms,
                        CONVERT(DECIMAL(10,2), ws.wait_time_ms * 100.0 / SUM(ws.wait_time_ms) OVER()) AS percentage_of_total_waits,
                        CASE 
                            WHEN ws.wait_type LIKE 'LCK%' THEN 'Lock'
                            WHEN ws.wait_type LIKE 'PAGEIOLATCH%' THEN 'I/O'
                            WHEN ws.wait_type LIKE 'WRITELOG%' THEN 'I/O'
                            WHEN ws.wait_type LIKE 'CXPACKET%' THEN 'Parallelism'
                            WHEN ws.wait_type LIKE 'SOS_SCHEDULER_YIELD%' THEN 'CPU'
                            WHEN ws.wait_type LIKE 'ASYNC_NETWORK_IO%' THEN 'Network'
                            WHEN ws.wait_type LIKE 'RESOURCE_SEMAPHORE%' THEN 'Memory'
                            WHEN ws.wait_type LIKE 'THREADPOOL%' THEN 'Worker Thread'
                            WHEN ws.wait_type LIKE 'BACKUP%' THEN 'Backup'
                            WHEN ws.wait_type LIKE 'DBMIRROR%' THEN 'Database Mirroring'
                            WHEN ws.wait_type LIKE 'HADR%' THEN 'AlwaysOn'
                            WHEN ws.wait_type LIKE 'REPLICA%' THEN 'Replication'
                            WHEN ws.wait_type LIKE 'LOG%' THEN 'Transaction Log'
                            WHEN ws.wait_type LIKE 'TEMP%' THEN 'TempDB'
                            ELSE 'Other'
                        END AS wait_category,
                        CASE 
                            WHEN ws.wait_type LIKE 'LCK%' THEN 'Lock contention detected'
                            WHEN ws.wait_type LIKE 'PAGEIOLATCH%' THEN 'I/O bottleneck - slow disk or missing indexes'
                            WHEN ws.wait_type LIKE 'WRITELOG%' THEN 'Transaction log write bottleneck'
                            WHEN ws.wait_type LIKE 'CXPACKET%' THEN 'Parallel query execution issues'
                            WHEN ws.wait_type LIKE 'SOS_SCHEDULER_YIELD%' THEN 'CPU pressure'
                            WHEN ws.wait_type LIKE 'ASYNC_NETWORK_IO%' THEN 'Network bottleneck or client processing delay'
                            WHEN ws.wait_type LIKE 'RESOURCE_SEMAPHORE%' THEN 'Memory pressure'
                            WHEN ws.wait_type LIKE 'THREADPOOL%' THEN 'Worker thread exhaustion'
                            ELSE 'Wait type requires further investigation'
                        END AS description,
                        CASE 
                            WHEN ws.wait_type LIKE 'LCK%' THEN 'Review transaction isolation levels, query optimization, and deadlocks'
                            WHEN ws.wait_type LIKE 'PAGEIOLATCH%' THEN 'Add appropriate indexes, optimize queries, check disk performance'
                            WHEN ws.wait_type LIKE 'WRITELOG%' THEN 'Optimize transaction size, separate log files, check disk performance'
                            WHEN ws.wait_type LIKE 'CXPACKET%' THEN 'Adjust MAXDOP settings, optimize parallel queries'
                            WHEN ws.wait_type LIKE 'SOS_SCHEDULER_YIELD%' THEN 'Add CPU resources, optimize CPU-intensive queries'
                            WHEN ws.wait_type LIKE 'ASYNC_NETWORK_IO%' THEN 'Optimize query result sets, check network bandwidth'
                            WHEN ws.wait_type LIKE 'RESOURCE_SEMAPHORE%' THEN 'Add memory, optimize memory-intensive queries'
                            WHEN ws.wait_type LIKE 'THREADPOOL%' THEN 'Adjust max worker threads, optimize blocking queries'
                            ELSE 'Investigate specific wait type causes'
                        END AS recommendation
                    FROM sys.dm_os_wait_stats ws
                    WHERE (@IncludeSystemWaits = 1 OR ws.wait_type NOT LIKE 'BROKER_%')
                    AND ws.wait_type NOT IN ('CLR_SEMAPHORE', 'LAZYWRITER_SLEEP', 'RESOURCE_QUEUE', 'SLEEP_TASK', 'SLEEP_SYSTEMTASK', 'SQLTRACE_BUFFER_FLUSH', 'WAITFOR', 'LOGMGR_QUEUE', 'CHECKPOINT_QUEUE', 'REQUEST_FOR_DEADLOCK_SEARCH', 'XE_TIMER_EVENT', 'XE_DISPATCHER_WAIT', 'FT_IFTS_SCHEDULER_IDLE_WAIT', 'BROKER_TO_FLUSH', 'BROKER_TASK_STOP', 'BROKER_EVENTHANDLER', 'SLEEP_BPOOL_FLUSH', 'BROKER_RECEIVE_WAITFOR', 'ONDEMAND_TASK_QUEUE', 'DBMIRRORING_CMD', 'DISPATCHER_QUEUE_SEMAPHORE', 'BROKER_RECEIVE_WAITFOR', 'BROKER_EVENTHANDLER')
                    AND ws.waiting_tasks_count > 0
                    ORDER BY ws.wait_time_ms DESC";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@TopN", topN);
                command.Parameters.AddWithValue("@IncludeSystemWaits", includeSystemWaits);

                using var reader = await command.ExecuteReaderAsync();
                
                var waitStats = new List<Dictionary<string, object>>();
                var categorySummary = new Dictionary<string, Dictionary<string, object>>();

                while (await reader.ReadAsync())
                {
                    var waitType = reader["wait_type"].ToString()!;
                    var waitCategory = reader["wait_category"].ToString()!;
                    
                    var stat = new Dictionary<string, object>
                    {
                        ["wait_type"] = waitType,
                        ["waiting_tasks_count"] = Convert.ToInt64(reader["waiting_tasks_count"]),
                        ["wait_time_ms"] = Convert.ToInt64(reader["wait_time_ms"]),
                        ["avg_wait_time_ms"] = Convert.ToDecimal(reader["avg_wait_time_ms"]),
                        ["percentage_of_total_waits"] = Convert.ToDecimal(reader["percentage_of_total_waits"]),
                        ["wait_category"] = waitCategory,
                        ["description"] = reader["description"].ToString()!,
                        ["recommendation"] = reader["recommendation"].ToString()!
                    };
                    waitStats.Add(stat);

                    // Update category summary
                    if (!categorySummary.ContainsKey(waitCategory))
                    {
                        categorySummary[waitCategory] = new Dictionary<string, object>
                        {
                            ["total_wait_time_ms"] = 0L,
                            ["total_waiting_tasks"] = 0L,
                            ["wait_type_count"] = 0
                        };
                    }

                    categorySummary[waitCategory]["total_wait_time_ms"] = Convert.ToInt64(categorySummary[waitCategory]["total_wait_time_ms"]) + Convert.ToInt64(reader["wait_time_ms"]);
                    categorySummary[waitCategory]["total_waiting_tasks"] = Convert.ToInt64(categorySummary[waitCategory]["total_waiting_tasks"]) + Convert.ToInt64(reader["waiting_tasks_count"]);
                    categorySummary[waitCategory]["wait_type_count"] = Convert.ToInt32(categorySummary[waitCategory]["wait_type_count"]) + 1;
                }

                // Reset wait stats if requested
                if (resetStats)
                {
                    try
                    {
                        using var resetCommand = new SqlCommand("DBCC SQLPERF('sys.dm_os_wait_stats', CLEAR)", connection);
                        await resetCommand.ExecuteNonQueryAsync();
                    }
                    catch (Exception resetEx)
                    {
                        // Log but don't fail the operation if reset fails
                        LoggingHelper.LogEnd(Guid.Empty, "GetWaitStats-Reset", false, sw.ElapsedMilliseconds, resetEx.Message);
                    }
                }

                var payload = new
                {
                    server_name = SqlConnectionManager.ServerName,
                    environment = SqlConnectionManager.Environment,
                    database = SqlConnectionManager.CurrentDatabase,
                    wait_statistics = waitStats,
                    category_summary = categorySummary,
                    parameters = new
                    {
                        top_n = topN,
                        include_system_waits = includeSystemWaits,
                        reset_stats = resetStats
                    }
                };
                sw.Stop();
                LoggingHelper.LogEnd(corr, "GetWaitStats", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "GetWaitStats");
                LoggingHelper.LogEnd(Guid.Empty, "GetWaitStats", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "GetWaitStats");
                LoggingHelper.LogEnd(Guid.Empty, "GetWaitStats", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }
    }
}
