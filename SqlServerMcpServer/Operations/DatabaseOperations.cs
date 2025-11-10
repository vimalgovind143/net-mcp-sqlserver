using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;
using SqlServerMcpServer.Configuration;
using SqlServerMcpServer.Utilities;
using System.ComponentModel;

namespace SqlServerMcpServer.Operations
{
    /// <summary>
    /// Handles database-level operations including health checks, database listing, and switching
    /// </summary>
    public static class DatabaseOperations
    {
        /// <summary>
        /// Check connection health and server info
        /// </summary>
        /// <returns>Server health information as JSON string</returns>
        [McpServerTool, Description("Check connection health and server info")]
        public static async Task<string> GetServerHealthAsync()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("GetServerHealth");
                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

                var query = @"
                    SELECT
                        SERVERPROPERTY('ServerName') AS server_name,
                        SERVERPROPERTY('ProductVersion') AS product_version,
                        SERVERPROPERTY('ProductLevel') AS product_level,
                        SERVERPROPERTY('Edition') AS edition,
                        DB_NAME() AS current_database,
                        SYSDATETIME() AS server_time";

                using var command = new SqlCommand(query, connection)
                {
                    CommandTimeout = SqlConnectionManager.CommandTimeout
                };
                using var reader = await command.ExecuteReaderAsync();

                var info = new Dictionary<string, object>();
                if (await reader.ReadAsync())
                {
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var columnName = reader.GetName(i);
                        var value = reader.GetValue(i);
                        info[columnName] = value is DBNull ? null : value;
                    }
                }

                var payload = new
                {
                    status = "ok",
                    connectivity = "reachable",
                    server = info,
                    environment = SqlConnectionManager.Environment,
                    server_name = SqlConnectionManager.ServerName,
                    database = SqlConnectionManager.CurrentDatabase
                };
                sw.Stop();
                LoggingHelper.LogEnd(corr, "GetServerHealth", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "GetServerHealth");
                LoggingHelper.LogEnd(Guid.Empty, "GetServerHealth", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "GetServerHealth");
                LoggingHelper.LogEnd(Guid.Empty, "GetServerHealth", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }

        /// <summary>
        /// Get current database connection info
        /// </summary>
        /// <returns>Current database information as JSON string</returns>
        [McpServerTool, Description("Get current database connection info")]
        public static string GetCurrentDatabase()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var corr = LoggingHelper.LogStart("GetCurrentDatabase");

            try
            {
                var payload = new
                {
                    server_name = SqlConnectionManager.ServerName,
                    environment = SqlConnectionManager.Environment,
                    current_database = SqlConnectionManager.CurrentDatabase,
                    connection_info = "Connected and ready",
                    security_mode = "READ_ONLY",
                    allowed_operations = new[]
                    {
                        "SELECT queries only",
                        "Database listing",
                        "Table schema inspection",
                        "Database switching"
                    }
                };

                sw.Stop();
                LoggingHelper.LogEnd(corr, "GetCurrentDatabase", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (Exception ex)
            {
                sw.Stop();
                LoggingHelper.LogEnd(corr, "GetCurrentDatabase", false, sw.ElapsedMilliseconds, ex.Message);

                var errorPayload = ResponseFormatter.CreateStandardErrorResponse("GetCurrentDatabase",
                    $"Failed to retrieve current database information: {ex.Message}");
                return ResponseFormatter.ToJson(errorPayload);
            }
        }

        /// <summary>
        /// Switch to a different database on the same server
        /// </summary>
        /// <param name="databaseName">The name of the database to switch to</param>
        /// <returns>Switch result as JSON string</returns>
        [McpServerTool, Description("Switch to a different database on the same server")]
        public static string SwitchDatabase([Description("The name of the database to switch to")] string databaseName)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var corr = LoggingHelper.LogStart("SwitchDatabase", databaseName);

            try
            {
                // Validate parameter
                if (string.IsNullOrWhiteSpace(databaseName))
                {
                    sw.Stop();
                    var validationContext = new ErrorContext(
                        ErrorCode.InvalidParameter,
                        "Database name cannot be empty or whitespace",
                        "SwitchDatabase"
                    );
                    validationContext.TroubleshootingSteps.Add("Provide a valid database name");
                    validationContext.SuggestedFixes.Add("Use GetDatabases to list available databases");
                    LoggingHelper.LogEnd(corr, "SwitchDatabase", false, sw.ElapsedMilliseconds, "Invalid parameter: empty database name");
                    var response = ResponseFormatter.CreateErrorContextResponse(validationContext, sw.ElapsedMilliseconds);
                    return ResponseFormatter.ToJson(response);
                }

                SqlConnectionManager.SwitchDatabase(databaseName);

                sw.Stop();
                LoggingHelper.LogEnd(corr, "SwitchDatabase", true, sw.ElapsedMilliseconds);

                var payload = new
                {
                    success = true,
                    message = $"Successfully switched to database: {databaseName}",
                    current_database = SqlConnectionManager.CurrentDatabase,
                    server_name = SqlConnectionManager.ServerName,
                    environment = SqlConnectionManager.Environment
                };
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "SwitchDatabase");

                // Add database-specific context
                if (sqlEx.Number == 911 || sqlEx.Number == 4060)
                {
                    context.SuggestedFixes.Add($"Verify that database '{databaseName}' exists on this server");
                    context.SuggestedFixes.Add("Use GetDatabases tool to see available databases");
                }
                else if (sqlEx.Number == 18456)
                {
                    context.SuggestedFixes.Add("Check your connection credentials and database permissions");
                }

                LoggingHelper.LogEnd(corr, "SwitchDatabase", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "SwitchDatabase");
                context.SuggestedFixes.Add("Ensure the database name is valid and you have access permissions");
                LoggingHelper.LogEnd(corr, "SwitchDatabase", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
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
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("GetDatabases", $"includeSystem:{includeSystemDatabases}, minSize:{minSizeMB}, state:{stateFilter}, name:{nameFilter}");

                // Validate state filter
                stateFilter = stateFilter?.ToUpperInvariant() ?? "ONLINE";
                if (!new[] { "ONLINE", "OFFLINE", "ALL" }.Contains(stateFilter))
                    stateFilter = "ONLINE";

                // Use master database connection for listing databases
                var masterConnectionString = SqlConnectionManager.CreateConnectionStringForDatabase("master");
                using var connection = new SqlConnection(masterConnectionString);
                await connection.OpenAsync();

                // Build dynamic WHERE clause
                var whereConditions = new List<string>();

                if (!includeSystemDatabases)
                {
                    whereConditions.Add("d.name NOT IN ('master', 'tempdb', 'model', 'msdb')");
                }

                if (minSizeMB.HasValue && minSizeMB.Value > 0)
                {
                    whereConditions.Add("SUM(mf.size * 8.0 / 1024) >= @minSizeMB");
                }

                if (stateFilter != "ALL")
                {
                    whereConditions.Add("d.state_desc = @stateFilter");
                }

                if (!string.IsNullOrEmpty(nameFilter))
                {
                    whereConditions.Add("d.name LIKE @nameFilter");
                }

                var whereClause = whereConditions.Count > 0 ? "WHERE " + string.Join(" AND ", whereConditions) : "";

                var query = $@"
                    SELECT
                        d.name AS database_name,
                        d.database_id,
                        d.create_date,
                        d.state_desc,
                        d.recovery_model_desc,
                        d.compatibility_level,
                        d.collation_name,
                        d.is_read_only,
                        d.user_access_desc,
                        SUM(mf.size * 8.0 / 1024) AS size_mb,
                        CASE WHEN d.name = @CurrentDb THEN 1 ELSE 0 END AS is_current,
                        (
                            SELECT COUNT(*)
                            FROM sys.tables t
                            WHERE t.object_id > 255 -- Exclude system tables
                        ) AS table_count,
                        (
                            SELECT COUNT(*)
                            FROM sys.views v
                            WHERE v.object_id > 255
                        ) AS view_count,
                        (
                            SELECT COUNT(*)
                            FROM sys.procedures p
                            WHERE p.object_id > 255
                        ) AS stored_procedure_count
                    FROM sys.databases d
                    LEFT JOIN sys.master_files mf ON d.database_id = mf.database_id
                    {whereClause}
                    GROUP BY d.name, d.database_id, d.create_date, d.state_desc,
                             d.recovery_model_desc, d.compatibility_level, d.collation_name,
                             d.is_read_only, d.user_access_desc
                    ORDER BY d.name";

                using var command = new SqlCommand(query, connection)
                {
                    CommandTimeout = SqlConnectionManager.CommandTimeout
                };
                command.Parameters.AddWithValue("@CurrentDb", SqlConnectionManager.CurrentDatabase);

                if (minSizeMB.HasValue)
                    command.Parameters.AddWithValue("@minSizeMB", minSizeMB.Value);
                if (stateFilter != "ALL")
                    command.Parameters.AddWithValue("@stateFilter", stateFilter);

                if (!string.IsNullOrEmpty(nameFilter))
                    command.Parameters.AddWithValue("@nameFilter", $"%{nameFilter}%");

                using var reader = await command.ExecuteReaderAsync();

                var databases = new List<Dictionary<string, object>>();

                while (await reader.ReadAsync())
                {
                    var database = new Dictionary<string, object>
                    {
                        ["database_name"] = reader["database_name"],
                        ["database_id"] = reader["database_id"],
                        ["create_date"] = reader["create_date"],
                        ["state_desc"] = reader["state_desc"],
                        ["recovery_model_desc"] = reader["recovery_model_desc"],
                        ["compatibility_level"] = reader["compatibility_level"],
                        ["collation_name"] = reader["collation_name"],
                        ["is_read_only"] = reader["is_read_only"],
                        ["user_access_desc"] = reader["user_access_desc"],
                        ["size_mb"] = reader["size_mb"] is DBNull ? 0.0m : Math.Round(Convert.ToDecimal(reader["size_mb"]), 2),
                        ["is_current"] = reader["is_current"],
                        ["object_summary"] = new Dictionary<string, object>
                        {
                            ["table_count"] = reader["table_count"],
                            ["view_count"] = reader["view_count"],
                            ["stored_procedure_count"] = reader["stored_procedure_count"]
                        }
                    };
                    databases.Add(database);
                }

                reader.Close();

                // Get backup information for each database
                var backupInfo = new Dictionary<string, object>();
                try
                {
                    var backupQuery = @"
                        SELECT
                            database_name,
                            MAX(backup_finish_date) AS last_backup_date,
                            type AS backup_type
                        FROM msdb.dbo.backupset
                        WHERE database_name IN (SELECT name FROM sys.databases)
                        GROUP BY database_name, type
                        ORDER BY database_name, type";

                    using var backupCommand = new SqlCommand(backupQuery, connection)
                    {
                        CommandTimeout = SqlConnectionManager.CommandTimeout
                    };

                    using var backupReader = await backupCommand.ExecuteReaderAsync();
                    while (await backupReader.ReadAsync())
                    {
                        var dbName = backupReader["database_name"].ToString();
                        if (!backupInfo.ContainsKey(dbName))
                        {
                            backupInfo[dbName] = new Dictionary<string, object>();
                        }

                        var backupType = backupReader["backup_type"].ToString();
                        var backupDate = backupReader["last_backup_date"];

                        ((Dictionary<string, object>)backupInfo[dbName])[backupType] =
                            backupDate is DBNull ? null : backupDate;
                    }
                }
                catch (Exception backupEx)
                {
                    // Backup information might not be accessible - log but continue
                    var backupContext = ErrorHelper.CreateErrorContextFromException(backupEx, "GetDatabases_BackupInfo");
                    LoggingHelper.LogEnd(Guid.Empty, "GetDatabases_BackupInfo", false, 0, backupEx.Message);
                    backupInfo["error"] = "Backup information not available";
                    backupInfo["error_details"] = backupEx.Message;
                    // Continue processing without backup information
                }

                // Add backup info to databases
                foreach (var db in databases)
                {
                    var dbName = db["database_name"].ToString();
                    if (backupInfo.ContainsKey(dbName))
                    {
                        db["backup_info"] = backupInfo[dbName];
                    }
                    else
                    {
                        db["backup_info"] = new Dictionary<string, object>
                        {
                            ["D"] = null, // Full backup
                            ["I"] = null  // Differential backup
                        };
                    }
                }

                var databasesData = new
                {
                    database_count = databases.Count,
                    filters_applied = new
                    {
                        include_system_databases = includeSystemDatabases,
                        min_size_mb = minSizeMB,
                        state_filter = stateFilter,
                        name_filter = nameFilter
                    },
                    databases = databases
                };

                var metadata = new Dictionary<string, object>
                {
                    ["row_count"] = databases.Count,
                    ["page_count"] = 1
                };

                var payload = ResponseFormatter.CreateStandardResponse("GetDatabases", databasesData, sw.ElapsedMilliseconds, metadata: metadata);
                sw.Stop();
                LoggingHelper.LogEnd(corr, "GetDatabases", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "GetDatabases");

                // Add context-specific guidance for database listing
                if (sqlEx.Number == 911 || sqlEx.Number == 4060)
                {
                    context.SuggestedFixes.Add("Verify you have SELECT permissions on sys.databases");
                    context.SuggestedFixes.Add("Check that the master database is accessible");
                }
                else if (sqlEx.Number == 229)
                {
                    context.SuggestedFixes.Add("Request VIEW SERVER STATE permission from your administrator");
                }

                LoggingHelper.LogEnd(Guid.Empty, "GetDatabases", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "GetDatabases");
                context.SuggestedFixes.Add("Verify connection to SQL Server is active");
                context.SuggestedFixes.Add("Check that you have permission to enumerate databases");

                LoggingHelper.LogEnd(Guid.Empty, "GetDatabases", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }
    }
}
