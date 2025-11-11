using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;
using SqlServerMcpServer.Configuration;
using SqlServerMcpServer.Utilities;
using System.ComponentModel;
using System.Data.SqlTypes;

namespace SqlServerMcpServer.Operations
{
    /// <summary>
    /// Diagnostic operations for database size, backup history, and error log insights
    /// </summary>
    public static class Diagnostics
    {
        /// <summary>
        /// Database size analysis with optional per-table breakdown
        /// </summary>
        /// <param name="includeTableBreakdown">Include table-level size breakdown (default: true)</param>
        /// <param name="topN">Maximum number of tables to include in the breakdown (default: 20)</param>
        /// <returns>Database size information as JSON string</returns>
        [McpServerTool, Description("Get database size summary and optional table breakdown")]
        public static async Task<string> GetDatabaseSize(
            [Description("Include table-level size breakdown (default: true)")] bool includeTableBreakdown = true,
            [Description("Maximum number of tables to include (default: 20)")] int topN = 20)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("GetDatabaseSize", $"includeTables:{includeTableBreakdown}, topN:{topN}");
                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

                // Totals: data/log sizes from sys.database_files
                const string totalsQuery = @"
                    SELECT 
                        CONVERT(decimal(18,2), SUM(size) * 8.0 / 1024) AS total_size_mb,
                        CONVERT(decimal(18,2), SUM(CASE WHEN type_desc = 'ROWS' THEN size ELSE 0 END) * 8.0 / 1024) AS data_size_mb,
                        CONVERT(decimal(18,2), SUM(CASE WHEN type_desc = 'LOG' THEN size ELSE 0 END) * 8.0 / 1024) AS log_size_mb
                    FROM sys.database_files";

                decimal totalSizeMb = 0, dataSizeMb = 0, logSizeMb = 0;
                using (var totalsCmd = new SqlCommand(totalsQuery, connection))
                using (var reader = await totalsCmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        totalSizeMb = reader["total_size_mb"] != DBNull.Value ? Convert.ToDecimal(reader["total_size_mb"]) : 0m;
                        dataSizeMb = reader["data_size_mb"] != DBNull.Value ? Convert.ToDecimal(reader["data_size_mb"]) : 0m;
                        logSizeMb  = reader["log_size_mb"]  != DBNull.Value ? Convert.ToDecimal(reader["log_size_mb"])  : 0m;
                    }
                }

                var tables = new List<Dictionary<string, object>>();
                if (includeTableBreakdown)
                {
                    const string tableQuery = @"
                        WITH TableSizes AS (
                            SELECT
                                s.name AS schema_name,
                                t.name AS table_name,
                                SUM(ps.row_count) AS row_count,
                                SUM(CASE WHEN i.index_id IN (0,1) THEN ps.reserved_page_count ELSE 0 END) AS data_pages,
                                SUM(CASE WHEN i.index_id NOT IN (0,1) THEN ps.reserved_page_count ELSE 0 END) AS index_pages
                            FROM sys.dm_db_partition_stats AS ps
                            INNER JOIN sys.tables AS t ON ps.object_id = t.object_id
                            INNER JOIN sys.schemas AS s ON t.schema_id = s.schema_id
                            LEFT JOIN sys.indexes AS i ON ps.object_id = i.object_id AND ps.index_id = i.index_id
                            GROUP BY s.name, t.name
                        )
                        SELECT TOP (@TopN)
                            schema_name,
                            table_name,
                            row_count,
                            CONVERT(decimal(18,2), data_pages * 8.0 / 1024) AS data_mb,
                            CONVERT(decimal(18,2), index_pages * 8.0 / 1024) AS index_mb,
                            CONVERT(decimal(18,2), (data_pages + index_pages) * 8.0 / 1024) AS total_mb
                        FROM TableSizes
                        ORDER BY total_mb DESC, schema_name, table_name";

                    using var tableCmd = new SqlCommand(tableQuery, connection);
                    tableCmd.Parameters.AddWithValue("@TopN", topN);
                    using var tr = await tableCmd.ExecuteReaderAsync();
                    while (await tr.ReadAsync())
                    {
                        tables.Add(new Dictionary<string, object>
                        {
                            ["schema_name"] = tr["schema_name"].ToString()!,
                            ["table_name"] = tr["table_name"].ToString()!,
                            ["row_count"] = Convert.ToInt64(tr["row_count"]),
                            ["data_mb"] = Convert.ToDecimal(tr["data_mb"]),
                            ["index_mb"] = Convert.ToDecimal(tr["index_mb"]),
                            ["total_mb"] = Convert.ToDecimal(tr["total_mb"]) 
                        });
                    }
                }

                var payload = new
                {
                    server_name = SqlConnectionManager.ServerName,
                    environment = SqlConnectionManager.Environment,
                    database = SqlConnectionManager.CurrentDatabase,
                    summary = new
                    {
                        total_size_mb = totalSizeMb,
                        data_size_mb = dataSizeMb,
                        log_size_mb = logSizeMb
                    },
                    tables = tables,
                    parameters = new
                    {
                        include_table_breakdown = includeTableBreakdown,
                        top_n = topN
                    }
                };

                sw.Stop();
                LoggingHelper.LogEnd(corr, "GetDatabaseSize", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "GetDatabaseSize");
                LoggingHelper.LogEnd(Guid.Empty, "GetDatabaseSize", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "GetDatabaseSize");
                LoggingHelper.LogEnd(Guid.Empty, "GetDatabaseSize", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }

        /// <summary>
        /// Backup history from msdb for the specified database
        /// </summary>
        /// <param name="databaseName">Database name filter (optional, defaults to current)</param>
        /// <param name="topN">Maximum number of backup records (default: 20)</param>
        /// <returns>Backup history as JSON string</returns>
        [McpServerTool, Description("Get backup history from msdb without executing procedures")]
        public static async Task<string> GetBackupHistory(
            [Description("Database name filter (optional - defaults to current database)")] string? databaseName = null,
            [Description("Maximum number of records to return (default: 20)")] int topN = 20)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("GetBackupHistory", $"db:{databaseName}, topN:{topN}");
                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

                if (string.IsNullOrWhiteSpace(databaseName))
                {
                    databaseName = SqlConnectionManager.CurrentDatabase;
                }

                const string query = @"
                    SELECT TOP (@TopN)
                        bs.database_name,
                        CASE bs.type WHEN 'D' THEN 'FULL' WHEN 'I' THEN 'DIFFERENTIAL' WHEN 'L' THEN 'LOG' ELSE bs.type END AS backup_type,
                        bs.backup_start_date,
                        bs.backup_finish_date,
                        DATEDIFF(SECOND, bs.backup_start_date, bs.backup_finish_date) AS duration_seconds,
                        CONVERT(decimal(18,2), bs.backup_size/1048576.0) AS backup_size_mb,
                        CONVERT(decimal(18,2), bs.compressed_backup_size/1048576.0) AS compressed_backup_size_mb,
                        bs.is_copy_only,
                        bs.recovery_model,
                        bmf.physical_device_name
                    FROM msdb.dbo.backupset bs
                    LEFT JOIN msdb.dbo.backupmediafamily bmf ON bs.media_set_id = bmf.media_set_id
                    WHERE (@DatabaseName IS NULL OR bs.database_name = @DatabaseName)
                    ORDER BY bs.backup_start_date DESC";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@TopN", topN);
                command.Parameters.AddWithValue("@DatabaseName", (object?)databaseName ?? DBNull.Value);

                var backups = new List<Dictionary<string, object>>();
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    backups.Add(new Dictionary<string, object>
                    {
                        ["database_name"] = reader["database_name"].ToString()!,
                        ["backup_type"] = reader["backup_type"].ToString()!,
                        ["backup_start_date"] = Convert.ToDateTime(reader["backup_start_date"]).ToString("o"),
                        ["backup_finish_date"] = Convert.ToDateTime(reader["backup_finish_date"]).ToString("o"),
                        ["duration_seconds"] = Convert.ToInt32(reader["duration_seconds"]),
                        ["backup_size_mb"] = Convert.ToDecimal(reader["backup_size_mb"]),
                        ["compressed_backup_size_mb"] = reader["compressed_backup_size_mb"] != DBNull.Value ? Convert.ToDecimal(reader["compressed_backup_size_mb"]) : 0m,
                        ["is_copy_only"] = Convert.ToBoolean(reader["is_copy_only"]),
                        ["recovery_model"] = reader["recovery_model"].ToString()!,
                        ["device"] = reader["physical_device_name"]?.ToString() ?? string.Empty
                    });
                }

                var payload = new
                {
                    server_name = SqlConnectionManager.ServerName,
                    environment = SqlConnectionManager.Environment,
                    database = SqlConnectionManager.CurrentDatabase,
                    backups = backups,
                    parameters = new
                    {
                        database_name = databaseName,
                        top_n = topN
                    }
                };

                sw.Stop();
                LoggingHelper.LogEnd(corr, "GetBackupHistory", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "GetBackupHistory");
                LoggingHelper.LogEnd(Guid.Empty, "GetBackupHistory", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "GetBackupHistory");
                LoggingHelper.LogEnd(Guid.Empty, "GetBackupHistory", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }

        /// <summary>
        /// Retrieves recent diagnostic events from the built-in 'system_health' extended events session.
        /// This avoids EXEC/xp_ procedures and remains read-only.
        /// </summary>
        /// <param name="topN">Maximum number of events to return (default: 100)</param>
        /// <returns>Error/diagnostic events as JSON string</returns>
        [McpServerTool, Description("Get recent diagnostic events from the system_health ring buffer (read-only)")]
        public static async Task<string> GetErrorLog(
            [Description("Maximum number of events to return (default: 100)")] int topN = 100)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("GetErrorLog", $"topN:{topN}");
                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

                // Read from system_health ring_buffer target (no EXEC/xp_ usage)
                const string query = @"
                    WITH TargetData AS (
                        SELECT CAST(t.target_data AS XML) AS target_data
                        FROM sys.dm_xe_session_targets AS t
                        JOIN sys.dm_xe_sessions AS s ON t.event_session_address = s.address
                        WHERE s.name = 'system_health' AND t.target_name = 'ring_buffer'
                    )
                    SELECT TOP (@TopN)
                        n.value('@name', 'nvarchar(256)') AS event_name,
                        n.value('@timestamp', 'datetime2') AS [timestamp],
                        n.query('.') AS event_xml
                    FROM TargetData
                    CROSS APPLY target_data.nodes('RingBufferTarget/event') AS q(n)
                    ORDER BY [timestamp] DESC";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@TopN", topN);

                var events = new List<Dictionary<string, object>>();
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var xmlOrdinal = reader.GetOrdinal("event_xml");
                    var sqlXml = reader.GetFieldValue<SqlXml>(xmlOrdinal);
                    var xmlString = sqlXml.IsNull ? string.Empty : sqlXml.Value;
                    events.Add(new Dictionary<string, object>
                    {
                        ["event_name"] = reader["event_name"].ToString()!,
                        ["timestamp"] = Convert.ToDateTime(reader["timestamp"]).ToString("o"),
                        ["event_xml"] = xmlString
                    });
                }

                var payload = new
                {
                    server_name = SqlConnectionManager.ServerName,
                    environment = SqlConnectionManager.Environment,
                    database = SqlConnectionManager.CurrentDatabase,
                    source = "system_health ring_buffer",
                    events = events,
                    parameters = new { top_n = topN }
                };

                sw.Stop();
                LoggingHelper.LogEnd(corr, "GetErrorLog", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "GetErrorLog");
                LoggingHelper.LogEnd(Guid.Empty, "GetErrorLog", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "GetErrorLog");
                LoggingHelper.LogEnd(Guid.Empty, "GetErrorLog", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }
    }
}
