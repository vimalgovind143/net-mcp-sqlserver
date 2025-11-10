using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Data;
using Serilog;

namespace SqlServerMcpServer
{
    [McpServerToolType]
    public static class SqlServerTools
    {
        private static string _currentConnectionString =
            Environment.GetEnvironmentVariable("SQLSERVER_CONNECTION_STRING")
            ?? GetConfigValue("SqlServer", "ConnectionString")
            ?? "Server=localhost;Database=master;Trusted_Connection=true;TrustServerCertificate=true;";

        private static string _currentDatabase = GetDatabaseFromConnectionString(_currentConnectionString);
        private static string _serverName = Environment.GetEnvironmentVariable("MCP_SERVER_NAME") ?? "SQL Server MCP";
        private static string _environment = Environment.GetEnvironmentVariable("MCP_ENVIRONMENT") ?? "unknown";
        private static int _commandTimeout = ParseIntEnv("SQLSERVER_COMMAND_TIMEOUT",
            ParseIntConfig("SqlServer", "CommandTimeout", 30));

        static SqlServerTools()
        {
            Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] SqlServerTools static constructor called");
            Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] Initializing with database: {_currentDatabase}");
        }

        private static int ParseIntEnv(string name, int defaultValue)
        {
            var val = Environment.GetEnvironmentVariable(name);
            return int.TryParse(val, out var parsed) && parsed > 0 ? parsed : defaultValue;
        }

        private static string? GetConfigValue(string section, string key)
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var candidatePaths = new[]
                {
                    Path.Combine(baseDir, "appsettings.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "SqlServerMcpServer", "appsettings.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json")
                };

                var path = candidatePaths.FirstOrDefault(File.Exists);
                if (path is null) return null;

                using var stream = File.OpenRead(path);
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.TryGetProperty(section, out var sec) &&
                    sec.TryGetProperty(key, out var val) &&
                    val.ValueKind == JsonValueKind.String)
                {
                    return val.GetString();
                }
            }
            catch { }
            return null;
        }

        private static int ParseIntConfig(string section, string key, int defaultValue)
        {
            try
            {
                var s = GetConfigValue(section, key);
                if (int.TryParse(s, out var parsed) && parsed > 0) return parsed;
            }
            catch { }
            return defaultValue;
        }

        private static void LogJson(object payload)
        {
            try
            {
                Log.Information("{@payload}", payload);
            }
            catch { }
        }

        private static Guid LogStart(string operation, string? context = null)
        {
            var id = Guid.NewGuid();
            LogJson(new
            {
                event_type = "start",
                correlation_id = id,
                operation,
                server_name = _serverName,
                environment = _environment,
                database = _currentDatabase,
                context,
                timestamp = DateTimeOffset.UtcNow
            });
            return id;
        }

        private static void LogEnd(Guid correlationId, string operation, bool success, long elapsedMs, string? error = null)
        {
            LogJson(new
            {
                event_type = "end",
                correlation_id = correlationId,
                operation,
                success,
                elapsed_ms = elapsedMs,
                server_name = _serverName,
                environment = _environment,
                database = _currentDatabase,
                error,
                timestamp = DateTimeOffset.UtcNow
            });
        }

        private static string GetDatabaseFromConnectionString(string connectionString)
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                return builder.InitialCatalog ?? "master";
            }
            catch
            {
                return "master";
            }
        }

        private static string CreateConnectionStringForDatabase(string databaseName)
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(_currentConnectionString)
                {
                    InitialCatalog = databaseName
                };
                return builder.ConnectionString;
            }
            catch
            {
                return _currentConnectionString;
            }
        }

        private static bool IsReadOnlyQuery(string query, out string blockedOperation)
        {
            blockedOperation = null;

            // Remove block and line comments for safer parsing
            var withoutBlockComments = Regex.Replace(query, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            var withoutLineComments = Regex.Replace(withoutBlockComments, @"--.*?$", string.Empty, RegexOptions.Multiline);

            // Normalize whitespace and case
            var normalizedQuery = Regex.Replace(withoutLineComments, @"\s+", " ").Trim();
            var upper = normalizedQuery.ToUpperInvariant();

            // Block multiple statements; allow a single trailing semicolon
            if (upper.Contains(";"))
            {
                var trimmedUpper = upper.Trim();
                var first = trimmedUpper.IndexOf(';');
                var last = trimmedUpper.LastIndexOf(';');
                var endsWithSemicolon = last == trimmedUpper.Length - 1;
                var multipleSemicolons = first != last;
                if (!endsWithSemicolon || multipleSemicolons)
                {
                    blockedOperation = "MULTIPLE_STATEMENTS";
                    return false;
                }
            }

            // Block dangerous operations and identify what was blocked
            var dangerousKeywords = new[] {
                "INSERT", "UPDATE", "DELETE", "DROP", "CREATE", "ALTER",
                "TRUNCATE", "EXEC", "EXECUTE", "MERGE", "BULK", "GRANT", "REVOKE", "DENY",
                "USE", "SET", "DBCC", "BACKUP", "RESTORE", "RECONFIGURE", "SP_CONFIGURE"
            };

            foreach (var keyword in dangerousKeywords)
            {
                if (Regex.IsMatch(upper, $@"\b{keyword}\b"))
                {
                    blockedOperation = keyword;
                    return false;
                }
            }

            // Allow CTEs starting with WITH provided there's a SELECT
            if (upper.StartsWith("WITH"))
            {
                if (!Regex.IsMatch(upper, @"\bSELECT\b"))
                {
                    blockedOperation = "NON_SELECT_STATEMENT";
                    return false;
                }
            }
            else if (!upper.StartsWith("SELECT"))
            {
                blockedOperation = "NON_SELECT_STATEMENT";
                return false;
            }

            // Block SELECT INTO (creates objects)
            if (Regex.IsMatch(upper, @"\bSELECT\b.*\bINTO\b"))
            {
                blockedOperation = "SELECT_INTO";
                return false;
            }

            return true;
        }

        [McpServerTool, Description("Check connection health and server info")]
        public static async Task<string> GetServerHealthAsync()
        {
            try
            {
                var corr = LogStart("GetServerHealth");
                var sw = Stopwatch.StartNew();
                using var connection = new SqlConnection(_currentConnectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT
                        SERVERPROPERTY('ServerName') AS server_name,
                        SERVERPROPERTY('ProductVersion') AS product_version,
                        SERVERPROPERTY('ProductLevel') AS product_level,
                        SERVERPROPERTY('Edition') AS edition,
                        DB_NAME() AS current_database,
                        SYSDATETIME() AS server_time";

                using var command = new SqlCommand(query, connection);
                command.CommandTimeout = _commandTimeout;
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
                    environment = _environment,
                    server_name = _serverName,
                    database = _currentDatabase
                };
                sw.Stop();
                LogEnd(corr, "GetServerHealth", true, sw.ElapsedMilliseconds);
                return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                LogEnd(Guid.Empty, "GetServerHealth", false, 0, ex.Message);
                return JsonSerializer.Serialize(new
                {
                    status = "error",
                    connectivity = "unreachable",
                    server_name = _serverName,
                    environment = _environment,
                    database = _currentDatabase,
                    error = ex.Message,
                    operation_type = "ERROR",
                    security_mode = "READ_ONLY_ENFORCED"
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        }

        [McpServerTool, Description("Get the current database connection info")]
        public static string GetCurrentDatabase()
        {
            var corr = LogStart("GetCurrentDatabase");
            var sw = Stopwatch.StartNew();

            try
            {
                var payload = new
                {
                    server_name = _serverName,
                    environment = _environment,
                    current_database = _currentDatabase,
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
                LogEnd(corr, "GetCurrentDatabase", true, sw.ElapsedMilliseconds);
                return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                sw.Stop();
                LogEnd(corr, "GetCurrentDatabase", false, sw.ElapsedMilliseconds, ex.Message);

                return JsonSerializer.Serialize(new
                {
                    server_name = _serverName,
                    environment = _environment,
                    database = _currentDatabase,
                    error = $"Failed to retrieve current database information: {ex.Message}",
                    operation_type = "ERROR",
                    security_mode = "READ_ONLY_ENFORCED"
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        }

        [McpServerTool, Description("Switch to a different database on the same server")]
        public static string SwitchDatabase([Description("The name of the database to switch to")] string databaseName)
        {
            var corr = LogStart("SwitchDatabase", databaseName);
            var sw = Stopwatch.StartNew();

            try
            {
                // Test connection to the new database first
                var testConnectionString = CreateConnectionStringForDatabase(databaseName);
                using var testConnection = new SqlConnection(testConnectionString);
                testConnection.Open();

                _currentConnectionString = testConnectionString;
                _currentDatabase = databaseName;

                sw.Stop();
                LogEnd(corr, "SwitchDatabase", true, sw.ElapsedMilliseconds);

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Successfully switched to database: {databaseName}",
                    current_database = _currentDatabase,
                    server_name = _serverName,
                    environment = _environment
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                sw.Stop();
                LogEnd(corr, "SwitchDatabase", false, sw.ElapsedMilliseconds, ex.Message);

                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Failed to switch to database {databaseName}: {ex.Message}",
                    current_database = _currentDatabase,
                    server_name = _serverName,
                    environment = _environment
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        }

        [McpServerTool, Description("Get a list of all databases on the SQL Server instance with size and backup information")]
        public static async Task<string> GetDatabasesAsync(
            [Description("Include system databases (optional, default: false)")] bool includeSystemDatabases = false,
            [Description("Filter by minimum size in MB (optional)")] decimal? minSizeMB = null,
            [Description("Filter by database state: 'ONLINE', 'OFFLINE', or 'ALL' (default: 'ONLINE')")] string? stateFilter = "ONLINE",
            [Description("Filter by database name (partial match, optional)")] string? nameFilter = null)
        {
            try
            {
                var corr = LogStart("GetDatabases", $"includeSystem:{includeSystemDatabases}, minSize:{minSizeMB}, state:{stateFilter}, name:{nameFilter}");
                var sw = Stopwatch.StartNew();
                
                // Validate state filter
                stateFilter = stateFilter?.ToUpperInvariant() ?? "ONLINE";
                if (!new[] { "ONLINE", "OFFLINE", "ALL" }.Contains(stateFilter))
                    stateFilter = "ONLINE";

                // Use master database connection for listing databases
                var masterConnectionString = CreateConnectionStringForDatabase("master");
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

                using var command = new SqlCommand(query, connection);
                command.CommandTimeout = _commandTimeout;
                command.Parameters.AddWithValue("@CurrentDb", _currentDatabase);
                
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

                    using var backupCommand = new SqlCommand(backupQuery, connection);
                    backupCommand.CommandTimeout = _commandTimeout;
                    
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
                catch
                {
                    // Backup information might not be accessible
                    backupInfo["error"] = "Backup information not available";
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

                var payload = CreateStandardResponse("GetDatabases", databasesData, sw.ElapsedMilliseconds, metadata: metadata);
                sw.Stop();
                LogEnd(corr, "GetDatabases", true, sw.ElapsedMilliseconds);
                return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                LogEnd(Guid.Empty, "GetDatabases", false, 0, ex.Message);
                var errorPayload = CreateStandardErrorResponse("GetDatabases", ex.Message);
                return JsonSerializer.Serialize(errorPayload, new JsonSerializerOptions { WriteIndented = true });
            }
        }

        [McpServerTool, Description("Execute a read-only SQL query on the current database with pagination and metadata")]
        public static async Task<string> ExecuteQueryAsync(
            [Description("The SQL query to execute (SELECT statements only)")] string query,
            [Description("Maximum rows to return (default 100, max 1000)")] int? maxRows = 100,
            [Description("Offset for pagination (default: 0)")] int? offset = 0,
            [Description("Page size for pagination (default: 100, max: 1000)")] int? pageSize = 100,
            [Description("Include query execution statistics (optional, default: false)")] bool includeStatistics = false)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var corr = LogStart("ExecuteQuery", $"query:{query.Substring(0, Math.Min(query.Length, 100))}...");
                
                // Validate and normalize parameters
                var effectivePageSize = Math.Min(pageSize ?? 100, 1000);
                var effectiveOffset = offset ?? 0;
                var effectiveMaxRows = Math.Min(maxRows ?? 100, 1000);
                
                // Use the smaller of maxRows and pageSize
                var limit = Math.Min(effectiveMaxRows, effectivePageSize);
                
                // Validate read-only operation
                if (!IsReadOnlyQuery(query, out string blockedOperation))
                {
                    var errorMessage = blockedOperation switch
                    {
                        "INSERT" => "❌ INSERT operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "UPDATE" => "❌ UPDATE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "DELETE" => "❌ DELETE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "DROP" => "❌ DROP operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "CREATE" => "❌ CREATE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "ALTER" => "❌ ALTER operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "TRUNCATE" => "❌ TRUNCATE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "EXEC" or "EXECUTE" => "❌ EXEC/EXECUTE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "MERGE" => "❌ MERGE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "BULK" => "❌ BULK operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "GRANT" => "❌ GRANT operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "REVOKE" => "❌ REVOKE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "DENY" => "❌ DENY operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "NON_SELECT_STATEMENT" => "❌ Only SELECT statements are allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        _ => $"❌ {blockedOperation} operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing."
                    };

                    var blockedPayload = CreateStandardBlockedResponse("ExecuteQuery", blockedOperation, query, errorMessage);
                    return JsonSerializer.Serialize(blockedPayload, new JsonSerializerOptions { WriteIndented = true });
                }

                // Generate query warnings
                var warnings = new List<string>();
                var upperQuery = query.ToUpperInvariant();
                
                if (!upperQuery.Contains("WHERE") && !upperQuery.Contains("TOP"))
                {
                    warnings.Add("Query may return large result set - consider adding WHERE clause or TOP limit");
                }
                
                if (effectiveOffset > 0 && !upperQuery.Contains("OFFSET"))
                {
                    warnings.Add("Using manual pagination - consider using OFFSET/FETCH in your query for better performance");
                }

                using var connection = new SqlConnection(_currentConnectionString);
                await connection.OpenAsync();

                // Enable statistics if requested
                if (includeStatistics)
                {
                    using var statsCommand = new SqlCommand("SET STATISTICS IO ON; SET STATISTICS TIME ON;", connection);
                    await statsCommand.ExecuteNonQueryAsync();
                }

                // Apply pagination and limits
                var finalQuery = ApplyPaginationAndLimit(query, limit, effectiveOffset);
                
                using var command = new SqlCommand(finalQuery, connection)
                {
                    CommandTimeout = _commandTimeout
                };

                // Execute query and get metadata
                var queryMetadata = new Dictionary<string, object>();
                var columnInfo = new List<Dictionary<string, object>>();
                
                using var reader = await command.ExecuteReaderAsync();
                
                // Get column information - only if we have a result set
                if (reader.HasRows)
                {
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var column = new Dictionary<string, object>
                        {
                            ["name"] = reader.GetName(i),
                            ["data_type"] = reader.GetDataTypeName(i),
                            ["sql_type"] = reader.GetFieldType(i).Name,
                            ["is_nullable"] = true, // Default to true for schema info
                            ["max_length"] = 0 // Would need schema query for accurate info
                        };
                        columnInfo.Add(column);
                    }
                }

                // Read results
                var results = new List<Dictionary<string, object>>();
                var rowCount = 0;
                
                while (await reader.ReadAsync() && rowCount < limit)
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var columnName = reader.GetName(i);
                        var value = reader.GetValue(i);
                        row[columnName] = value is DBNull ? null : value;
                    }
                    results.Add(row);
                    rowCount++;
                }

                reader.Close();

                // Get execution statistics if requested
                var statistics = new Dictionary<string, object>();
                if (includeStatistics)
                {
                    try
                    {
                        // Reset statistics
                        using var resetCommand = new SqlCommand("SET STATISTICS IO OFF; SET STATISTICS TIME OFF;", connection);
                        await resetCommand.ExecuteNonQueryAsync();
                        
                        // Note: In a real implementation, you would capture the statistics output
                        // This is a simplified version
                        statistics["logical_reads"] = "N/A";
                        statistics["physical_reads"] = "N/A";
                        statistics["cpu_time_ms"] = sw.ElapsedMilliseconds;
                        statistics["elapsed_time_ms"] = sw.ElapsedMilliseconds;
                    }
                    catch
                    {
                        statistics["error"] = "Statistics collection failed";
                    }
                }

                // Build query metadata
                queryMetadata["execution_time_ms"] = sw.ElapsedMilliseconds;
                queryMetadata["rows_affected"] = rowCount;
                queryMetadata["columns_returned"] = reader.FieldCount;
                queryMetadata["query_hash"] = query.GetHashCode().ToString();
                
                // Calculate pagination info
                var pagination = new Dictionary<string, object>
                {
                    ["current_page"] = effectiveOffset / effectivePageSize + 1,
                    ["page_size"] = effectivePageSize,
                    ["offset"] = effectiveOffset,
                    ["has_more"] = rowCount == effectivePageSize
                };

                var queryData = new
                {
                    query_metadata = queryMetadata,
                    columns = columnInfo,
                    pagination = pagination,
                    statistics = includeStatistics ? statistics : null,
                    data = results,
                    applied_limit = limit
                };

                var recommendations = new List<string>();
                if (warnings.Any())
                {
                    recommendations.Add("Consider optimizing your query for better performance");
                }

                var payload = CreateStandardResponse("ExecuteQuery", queryData, sw.ElapsedMilliseconds, 
                    warnings: warnings.Any() ? warnings : null, recommendations: recommendations);
                
                sw.Stop();
                LogEnd(corr, "ExecuteQuery", true, sw.ElapsedMilliseconds);
                return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                LogEnd(Guid.Empty, "ExecuteQuery", false, sw.ElapsedMilliseconds, sqlEx.Message);
                
                var errorDetails = new Dictionary<string, object>
                {
                    ["error_number"] = sqlEx.Number,
                    ["error_line"] = sqlEx.LineNumber,
                    ["error_message"] = sqlEx.Message,
                    ["suggestion"] = GetErrorSuggestion(sqlEx.Number)
                };

                var errorPayload = CreateStandardErrorResponse("ExecuteQuery", $"SQL Error: {sqlEx.Message}", 
                    sw.ElapsedMilliseconds, "SQL_ERROR", errorDetails);
                return JsonSerializer.Serialize(errorPayload, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                sw.Stop();
                LogEnd(Guid.Empty, "ExecuteQuery", false, sw.ElapsedMilliseconds, ex.Message);
                var errorPayload = CreateStandardErrorResponse("ExecuteQuery", $"Error: {ex.Message}", sw.ElapsedMilliseconds);
                return JsonSerializer.Serialize(errorPayload, new JsonSerializerOptions { WriteIndented = true });
            }
        }

<<<<<<< HEAD
        private static string ApplyPaginationAndLimit(string query, int limit, int offset)
=======
        [McpServerTool, Description("Execute a read-only SQL query with formatting and parameters (SRS: read_query)")]
        public static async Task<string> ReadQueryAsync(
            [Description("T-SQL SELECT statement (read-only)")] string query,
            [Description("Per-call timeout in seconds (default 30, range 1–300)")] int? timeout = null,
            [Description("Maximum rows to return (default 1000, range 1–10000)")] int? max_rows = 1000,
            [Description("Result format: json | csv | table (HTML)")] string? format = "json",
            [Description("Named parameters to bind (e.g., { id: 42 })")] Dictionary<string, object>? parameters = null,
            [Description("CSV delimiter (default ','. Use 'tab' or \\t for tab)")] string? delimiter = null)
        {
            try
            {
                var corr = LogStart("ReadQuery", query);
                var sw = Stopwatch.StartNew();

                // Enforce read-only
                if (!IsReadOnlyQuery(query, out string blockedOperation))
                {
                    var errorMessage = blockedOperation switch
                    {
                        "INSERT" => "❌ INSERT operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "UPDATE" => "❌ UPDATE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "DELETE" => "❌ DELETE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "DROP" => "❌ DROP operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "CREATE" => "❌ CREATE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "ALTER" => "❌ ALTER operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "TRUNCATE" => "❌ TRUNCATE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "EXEC" or "EXECUTE" => "❌ EXEC/EXECUTE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "MERGE" => "❌ MERGE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "BULK" => "❌ BULK operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "GRANT" => "❌ GRANT operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "REVOKE" => "❌ REVOKE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "DENY" => "❌ DENY operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "SELECT_INTO" => "❌ SELECT INTO is not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        "MULTIPLE_STATEMENTS" => "❌ Multiple statements are not allowed. Submit a single SELECT statement.",
                        "NON_SELECT_STATEMENT" => "❌ Only SELECT statements are allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                        _ => $"❌ {blockedOperation} operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing."
                    };

                    return JsonSerializer.Serialize(new
                    {
                        server_name = _serverName,
                        environment = _environment,
                        database = _currentDatabase,
                        error = errorMessage,
                        blocked_operation = blockedOperation,
                        blocked_query = query,
                        operation_type = "BLOCKED",
                        security_mode = "READ_ONLY_ENFORCED",
                        allowed_operations = new[] { "SELECT queries for data retrieval", "Database listing", "Table schema inspection", "Database switching" },
                        help = "This MCP server is configured for READ-ONLY access to prevent accidental data modification. Use SELECT statements to query data.",
                        format = format?.ToLowerInvariant()
                    }, new JsonSerializerOptions { WriteIndented = true });
                }

                // Validate and normalize parameters
                var fmt = string.IsNullOrWhiteSpace(format) ? "json" : format.Trim().ToLowerInvariant();
                if (fmt != "json" && fmt != "csv" && fmt != "table")
                {
                    return JsonSerializer.Serialize(new
                    {
                        server_name = _serverName,
                        environment = _environment,
                        database = _currentDatabase,
                        error = "Invalid format. Allowed values: json, csv, table",
                        operation_type = "VALIDATION_ERROR",
                        security_mode = "READ_ONLY_ENFORCED"
                    }, new JsonSerializerOptions { WriteIndented = true });
                }

                int appliedTimeout = _commandTimeout;
                if (timeout.HasValue)
                {
                    appliedTimeout = Math.Clamp(timeout.Value, 1, 300);
                }

                int requestedMax = max_rows ?? 1000;
                int appliedMaxRows = Math.Clamp(requestedMax, 1, 10000);

                var finalQuery = ApplyTopLimit(query, appliedMaxRows);

                using var connection = new SqlConnection(_currentConnectionString);
                await connection.OpenAsync();

                using var command = new SqlCommand(finalQuery, connection)
                {
                    CommandTimeout = appliedTimeout
                };

                // Bind parameters if provided
                if (parameters is not null)
                {
                    foreach (var kvp in parameters)
                    {
                        var name = kvp.Key?.Trim();
                        if (string.IsNullOrEmpty(name)) continue;
                        if (!name.StartsWith("@")) name = "@" + name;
                        var value = kvp.Value ?? DBNull.Value;
                        command.Parameters.AddWithValue(name, value);
                    }
                }

                using var reader = await command.ExecuteReaderAsync();

                // Column metadata
                var columns = new List<Dictionary<string, object>>();
                var columnNames = new List<string>();
                try
                {
                    var schema = reader.GetSchemaTable();
                    if (schema is not null)
                    {
                        foreach (DataRow row in schema.Rows)
                        {
                            var name = row["ColumnName"]?.ToString() ?? string.Empty;
                            var type = (row["DataType"] as Type)?.FullName ?? (row["DataType"]?.ToString() ?? "");
                            var allowNull = row.Table.Columns.Contains("AllowDBNull") ? (row["AllowDBNull"] as bool? ?? false) : false;
                            var size = row.Table.Columns.Contains("ColumnSize") ? (row["ColumnSize"] as int? ?? 0) : 0;
                            columnNames.Add(name);
                            columns.Add(new Dictionary<string, object>
                            {
                                ["name"] = name,
                                ["data_type"] = type,
                                ["allow_null"] = allowNull,
                                ["size"] = size
                            });
                        }
                    }
                    else
                    {
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            var name = reader.GetName(i);
                            columnNames.Add(name);
                            columns.Add(new Dictionary<string, object>
                            {
                                ["name"] = name,
                                ["data_type"] = reader.GetFieldType(i).FullName ?? reader.GetFieldType(i).Name,
                                ["allow_null"] = true,
                                ["size"] = 0
                            });
                        }
                    }
                }
                catch
                {
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var name = reader.GetName(i);
                        columnNames.Add(name);
                        columns.Add(new Dictionary<string, object>
                        {
                            ["name"] = name,
                            ["data_type"] = reader.GetFieldType(i).FullName ?? reader.GetFieldType(i).Name,
                            ["allow_null"] = true,
                            ["size"] = 0
                        });
                    }
                }

                // Read results
                var results = new List<Dictionary<string, object>>();
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var colName = reader.GetName(i);
                        var value = reader.GetValue(i);
                        row[colName] = value is DBNull ? null : value;
                    }
                    results.Add(row);
                }

                string? rendered = null;
                char delim = ParseDelimiter(delimiter);
                if (fmt == "csv")
                {
                    rendered = ToCsv(results, columnNames, delim);
                }
                else if (fmt == "table")
                {
                    rendered = ToHtmlTable(results, columnNames);
                }

                sw.Stop();
                LogEnd(corr, "ReadQuery", true, sw.ElapsedMilliseconds);

                // Ensure consistent typing for conditional result selection
                object resultData = fmt == "json" ? (object)results : (object)(rendered ?? string.Empty);

                var payload = new
                {
                    server_name = _serverName,
                    environment = _environment,
                    database = _currentDatabase,
                    row_count = results.Count,
                    elapsed_ms = sw.ElapsedMilliseconds,
                    operation_type = "READ_QUERY",
                    security_mode = "READ_ONLY_ENFORCED",
                    format = fmt,
                    columns,
                    applied_limit = appliedMaxRows,
                    result = resultData
                };

                return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                LogEnd(Guid.Empty, "ReadQuery", false, 0, ex.Message);
                return JsonSerializer.Serialize(new
                {
                    server_name = _serverName,
                    environment = _environment,
                    database = _currentDatabase,
                    error = $"SQL Error: {ex.Message}",
                    operation_type = "ERROR",
                    security_mode = "READ_ONLY_ENFORCED",
                    help = "Check your SQL syntax and ensure you're only using SELECT statements.",
                    format = format?.ToLowerInvariant()
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        }

        private static string ApplyTopLimit(string query, int maxRows)
>>>>>>> 0c8100b5a19414a6346c7804e9335af53a1388fd
        {
            try
            {
                // Remove comments for safer parsing
                var withoutComments = Regex.Replace(query, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
                withoutComments = Regex.Replace(withoutComments, @"--.*?$", string.Empty, RegexOptions.Multiline);

                // If query already has OFFSET/FETCH, modify it
                if (Regex.IsMatch(withoutComments, @"\bOFFSET\s+\d+\s+ROWS\b", RegexOptions.IgnoreCase))
                {
                    // Query already has pagination, just adjust the limit
                    var fetchMatch = Regex.Match(withoutComments, @"\bFETCH\s+NEXT\s+(\d+)\s+ROWS\s+ONLY\b", RegexOptions.IgnoreCase);
                    if (fetchMatch.Success && int.TryParse(fetchMatch.Groups[1].Value, out var existingFetch))
                    {
                        var newFetch = Math.Min(existingFetch, limit);
                        return Regex.Replace(
                            withoutComments,
                            @"\bFETCH\s+NEXT\s+\d+\s+ROWS\s+ONLY\b",
                            $"FETCH NEXT {newFetch} ROWS ONLY",
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                            TimeSpan.FromMilliseconds(100)
                        );
                    }
                    return query;
                }

                // If query has TOP, modify it
                var topMatch = Regex.Match(withoutComments, @"\bSELECT\s+(DISTINCT\s+)?TOP\s+(\d+)\b", RegexOptions.IgnoreCase);
                if (topMatch.Success)
                {
                    if (int.TryParse(topMatch.Groups[2].Value, out var existing))
                    {
                        var newTop = Math.Min(existing, limit);
                        if (newTop != existing)
                        {
                            var capped = Regex.Replace(
                                withoutComments,
                                @"\bSELECT\s+(DISTINCT\s+)?TOP\s+\d+\b",
                                m =>
                                {
                                    var distinct = m.Groups[1].Value;
                                    return $"SELECT {distinct}TOP {newTop}";
                                },
                                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                                TimeSpan.FromMilliseconds(100)
                            );
                            return string.IsNullOrWhiteSpace(capped) ? query : capped;
                        }
                    }
                    return query;
                }

                // Add OFFSET/FETCH for pagination if offset > 0
                if (offset > 0)
                {
                    var withOffset = Regex.Replace(
                        withoutComments,
                        @"\bSELECT\s+(DISTINCT\s+)?",
                        m =>
                        {
                            var distinct = m.Groups[1].Value;
                            return $"SELECT {distinct}";
                        },
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                        TimeSpan.FromMilliseconds(100)
                    );
                    
                    return $"{withOffset} OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY";
                }

                // No pagination or TOP: insert TOP limit after first SELECT
                var replaced = Regex.Replace(
                    withoutComments,
                    @"\bSELECT\s+(DISTINCT\s+)?",
                    m =>
                    {
                        var distinct = m.Groups[1].Value;
                        return $"SELECT {distinct}TOP {limit} ";
                    },
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100)
                );

                return string.IsNullOrWhiteSpace(replaced) ? query : replaced;
            }
            catch
            {
                return query;
            }
        }

<<<<<<< HEAD
        private static string GetErrorSuggestion(int errorNumber)
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

        private static object CreateStandardResponse(string operation, object data, long executionTimeMs, 
            List<string> warnings = null, List<string> recommendations = null, 
            Dictionary<string, object> metadata = null)
        {
            return new
            {
                server_name = _serverName,
                environment = _environment,
                database = _currentDatabase,
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

        private static object CreateStandardErrorResponse(string operation, string error, long executionTimeMs = 0,
            string errorCode = "SQL_ERROR", Dictionary<string, object> errorDetails = null,
            List<string> troubleshootingSteps = null)
        {
            return new
            {
                server_name = _serverName,
                environment = _environment,
                database = _currentDatabase,
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

        private static object CreateStandardBlockedResponse(string operation, string blockedOperation, 
            string query, string errorMessage)
        {
            return new
            {
                server_name = _serverName,
                environment = _environment,
                database = _currentDatabase,
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

        [McpServerTool, Description("Get a list of all tables in the current database with size information and filtering options")]
        public static async Task<string> GetTablesAsync(
            [Description("Filter tables by schema name (optional)")] string? schemaFilter = null,
            [Description("Filter by table name (partial match, optional)")] string? nameFilter = null,
            [Description("Minimum row count filter (optional)")] int? minRowCount = null,
            [Description("Sort by: 'NAME', 'SIZE', or 'ROWS' (default: 'NAME')")] string? sortBy = "NAME",
            [Description("Sort order: 'ASC' or 'DESC' (default: 'ASC')")] string? sortOrder = "ASC")
=======
        private static char ParseDelimiter(string? delimiter)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(delimiter)) return ',';
                var d = delimiter.Trim();
                if (string.Equals(d, "tab", StringComparison.OrdinalIgnoreCase)) return '\t';
                if (string.Equals(d, "\\t", StringComparison.OrdinalIgnoreCase)) return '\t';
                return d.Length == 1 ? d[0] : ',';
            }
            catch { return ','; }
        }

        private static string ToCsv(List<Dictionary<string, object>> rows, List<string> columns, char delimiter)
        {
            var sb = new StringBuilder();
            // Header
            for (int i = 0; i < columns.Count; i++)
            {
                if (i > 0) sb.Append(delimiter);
                sb.Append(EscapeCsv(columns[i], delimiter));
            }
            sb.AppendLine();

            foreach (var row in rows)
            {
                for (int i = 0; i < columns.Count; i++)
                {
                    if (i > 0) sb.Append(delimiter);
                    var col = columns[i];
                    row.TryGetValue(col, out var value);
                    var s = value is null ? null : Convert.ToString(value);
                    sb.Append(EscapeCsv(s, delimiter));
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string EscapeCsv(string? value, char delimiter)
        {
            if (value is null) return "NULL";
            var needsQuote = value.Contains('"') || value.Contains('\n') || value.Contains('\r') || value.Contains(delimiter) || (value.Length > 0 && (value[0] == ' ' || value[^1] == ' '));
            if (!needsQuote) return value;
            var escaped = value.Replace("\"", "\"\"");
            return "\"" + escaped + "\"";
        }

        private static string ToHtmlTable(List<Dictionary<string, object>> rows, List<string> columns)
        {
            var sb = new StringBuilder();
            sb.Append("<table>");
            sb.Append("<thead><tr>");
            foreach (var col in columns)
            {
                sb.Append("<th>").Append(EscapeHtml(col)).Append("</th>");
            }
            sb.Append("</tr></thead>");
            sb.Append("<tbody>");
            foreach (var row in rows)
            {
                sb.Append("<tr>");
                foreach (var col in columns)
                {
                    row.TryGetValue(col, out var value);
                    var s = value is null ? "NULL" : Convert.ToString(value) ?? string.Empty;
                    sb.Append("<td>").Append(EscapeHtml(s)).Append("</td>");
                }
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table>");
            return sb.ToString();
        }

        private static string EscapeHtml(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }

        [McpServerTool, Description("Get a list of all tables in the current database")]
        public static async Task<string> GetTablesAsync()
>>>>>>> 0c8100b5a19414a6346c7804e9335af53a1388fd
        {
            try
            {
                var corr = LogStart("GetTables", $"schema:{schemaFilter}, name:{nameFilter}, minRows:{minRowCount}, sort:{sortBy} {sortOrder}");
                var sw = Stopwatch.StartNew();
                using var connection = new SqlConnection(_currentConnectionString);
                await connection.OpenAsync();

                // Validate sort parameters
                sortBy = sortBy?.ToUpperInvariant() ?? "NAME";
                sortOrder = sortOrder?.ToUpperInvariant() ?? "ASC";
                
                if (!new[] { "NAME", "SIZE", "ROWS" }.Contains(sortBy))
                    sortBy = "NAME";
                if (!new[] { "ASC", "DESC" }.Contains(sortOrder))
                    sortOrder = "ASC";

                // Build dynamic WHERE clause for filters
                var whereConditions = new List<string>();
                if (!string.IsNullOrEmpty(schemaFilter))
                {
                    whereConditions.Add("s.name = @schemaFilter");
                }
                if (!string.IsNullOrEmpty(nameFilter))
                {
                    whereConditions.Add("t.name LIKE @nameFilter");
                }
                if (minRowCount.HasValue && minRowCount.Value > 0)
                {
                    whereConditions.Add("p.rows >= @minRowCount");
                }

                // Always include the index condition
                whereConditions.Add("i.index_id IN (0,1)");

                var whereClause = "WHERE " + string.Join(" AND ", whereConditions);

                // Build dynamic ORDER BY clause
                var orderByClause = sortBy switch
                {
                    "SIZE" => $"ORDER BY total_size_mb {(sortOrder == "DESC" ? "DESC" : "ASC")}, s.name, t.name",
                    "ROWS" => $"ORDER BY p.rows {(sortOrder == "DESC" ? "DESC" : "ASC")}, s.name, t.name",
                    _ => $"ORDER BY s.name {(sortOrder == "DESC" ? "DESC" : "ASC")}, t.name"
                };

                var query = $@"
                    SELECT
                        t.name AS table_name,
                        s.name AS schema_name,
                        p.rows AS row_count,
                        SUM(a.total_pages) * 8 / 1024.0 AS total_size_mb,
                        SUM(a.used_pages) * 8 / 1024.0 AS used_size_mb,
                        SUM(a.data_pages) * 8 / 1024.0 AS data_size_mb,
                        (SUM(a.total_pages) - SUM(a.used_pages)) * 8 / 1024.0 AS unused_size_mb,
                        t.create_date,
                        t.modify_date,
                        t.is_memory_optimized,
                        t.temporal_type_desc,
                        (
                            SELECT COUNT(*) 
                            FROM sys.indexes i 
                            WHERE i.object_id = t.object_id AND i.is_primary_key = 1
                        ) AS has_primary_key,
                        (
                            SELECT COUNT(*) 
                            FROM sys.indexes i 
                            WHERE i.object_id = t.object_id AND i.type = 1
                        ) AS has_clustered_index,
                        (
                            SELECT COUNT(*)
                            FROM sys.foreign_keys fk
                            WHERE fk.parent_object_id = t.object_id
                        ) AS foreign_key_count,
                        (
                            SELECT COUNT(*)
                            FROM sys.foreign_keys fk
                            WHERE fk.referenced_object_id = t.object_id
                        ) AS referenced_by_count
                    FROM sys.tables t
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    INNER JOIN sys.indexes i ON t.object_id = i.object_id
                    INNER JOIN sys.partitions p ON i.object_id = p.object_id AND i.index_id = p.index_id
                    INNER JOIN sys.allocation_units a ON p.partition_id = a.container_id
                    {whereClause}
                    GROUP BY t.object_id, t.name, s.name, p.rows, t.create_date, t.modify_date, t.is_memory_optimized, t.temporal_type_desc
                    {orderByClause}";

                using var command = new SqlCommand(query, connection);
                command.CommandTimeout = _commandTimeout;
                
                if (!string.IsNullOrEmpty(schemaFilter))
                    command.Parameters.AddWithValue("@schemaFilter", schemaFilter);
                if (!string.IsNullOrEmpty(nameFilter))
                    command.Parameters.AddWithValue("@nameFilter", $"%{nameFilter}%");
                if (minRowCount.HasValue)
                    command.Parameters.AddWithValue("@minRowCount", minRowCount.Value);

                using var reader = await command.ExecuteReaderAsync();

                var tables = new List<Dictionary<string, object>>();

                while (await reader.ReadAsync())
                {
                    var table = new Dictionary<string, object>
                    {
                        ["table_name"] = reader["table_name"],
                        ["schema_name"] = reader["schema_name"],
                        ["row_count"] = reader["row_count"],
                        ["total_size_mb"] = Math.Round(Convert.ToDecimal(reader["total_size_mb"]), 2),
                        ["used_size_mb"] = Math.Round(Convert.ToDecimal(reader["used_size_mb"]), 2),
                        ["data_size_mb"] = Math.Round(Convert.ToDecimal(reader["data_size_mb"]), 2),
                        ["unused_size_mb"] = Math.Round(Convert.ToDecimal(reader["unused_size_mb"]), 2),
                        ["create_date"] = reader["create_date"],
                        ["modify_date"] = reader["modify_date"],
                        ["is_memory_optimized"] = reader["is_memory_optimized"],
                        ["temporal_type_desc"] = reader["temporal_type_desc"] is DBNull ? null : reader["temporal_type_desc"],
                        ["index_summary"] = new Dictionary<string, object>
                        {
                            ["index_count"] = 0, // Will be calculated separately if needed
                            ["has_primary_key"] = Convert.ToInt32(reader["has_primary_key"]) > 0,
                            ["has_clustered_index"] = Convert.ToInt32(reader["has_clustered_index"]) > 0
                        },
                        ["relationship_summary"] = new Dictionary<string, object>
                        {
                            ["foreign_keys_referencing"] = reader["foreign_key_count"],
                            ["foreign_keys_referenced_by"] = reader["referenced_by_count"]
                        }
                    };
                    tables.Add(table);
                }

                var tablesData = new
                {
                    table_count = tables.Count,
                    filters_applied = new
                    {
                        schema_filter = schemaFilter,
                        name_filter = nameFilter,
                        min_row_count = minRowCount,
                        sort_by = sortBy,
                        sort_order = sortOrder
                    },
                    tables = tables
                };

                var metadata = new Dictionary<string, object>
                {
                    ["row_count"] = tables.Count,
                    ["page_count"] = 1
                };

                var payload = CreateStandardResponse("GetTables", tablesData, sw.ElapsedMilliseconds, metadata: metadata);
                sw.Stop();
                LogEnd(corr, "GetTables", true, sw.ElapsedMilliseconds);
                return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                LogEnd(Guid.Empty, "GetTables", false, 0, ex.Message);
                var errorPayload = CreateStandardErrorResponse("GetTables", ex.Message);
                return JsonSerializer.Serialize(errorPayload, new JsonSerializerOptions { WriteIndented = true });
            }
        }

        [McpServerTool, Description("Get the schema information for a specific table with PK/FK info, indexes, and extended properties")]
        public static async Task<string> GetTableSchemaAsync(
            [Description("Name of the table")] string tableName,
            [Description("Schema name (defaults to 'dbo')")] string? schemaName = "dbo",
            [Description("Include column statistics (optional, default: false)")] bool includeStatistics = false)
        {
            try
            {
                var corr = LogStart("GetTableSchema", $"{schemaName}.{tableName}");
                var sw = Stopwatch.StartNew();
                using var connection = new SqlConnection(_currentConnectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT
                        c.name AS column_name,
                        t.name AS data_type,
                        c.max_length,
                        c.precision,
                        c.scale,
                        c.is_nullable,
                        c.is_identity,
                        c.is_computed,
                        CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS is_primary_key,
                        pk.key_ordinal AS pk_ordinal,
                        dc.definition AS default_value,
                        cc.definition AS computed_definition,
                        ep.value AS column_description
                    FROM sys.columns c
                    INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
                    INNER JOIN sys.tables tbl ON c.object_id = tbl.object_id
                    INNER JOIN sys.schemas s ON tbl.schema_id = s.schema_id
                    LEFT JOIN (
                        SELECT ic.object_id, ic.column_id, ic.key_ordinal
                        FROM sys.index_columns ic
                        INNER JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                        WHERE i.is_primary_key = 1
                    ) pk ON c.object_id = pk.object_id AND c.column_id = pk.column_id
                    LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
                    LEFT JOIN sys.computed_columns cc ON c.object_id = cc.object_id AND c.column_id = cc.column_id
                    LEFT JOIN sys.extended_properties ep ON ep.major_id = tbl.object_id 
                        AND ep.minor_id = c.column_id AND ep.name = 'MS_Description'
                    WHERE tbl.name = @tableName AND s.name = @schemaName
                    ORDER BY c.column_id";

                using var command = new SqlCommand(query, connection);
                command.CommandTimeout = _commandTimeout;
                command.Parameters.AddWithValue("@tableName", tableName);
                command.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                using var reader = await command.ExecuteReaderAsync();

                var columns = new List<Dictionary<string, object>>();

                while (await reader.ReadAsync())
                {
                    var column = new Dictionary<string, object>
                    {
                        ["column_name"] = reader["column_name"] ?? "",
                        ["data_type"] = reader["data_type"] ?? "",
                        ["max_length"] = reader["max_length"] ?? 0,
                        ["precision"] = reader["precision"] ?? 0,
                        ["scale"] = reader["scale"] ?? 0,
                        ["is_nullable"] = reader["is_nullable"] ?? false,
                        ["is_identity"] = reader["is_identity"] ?? false,
                        ["is_computed"] = reader["is_computed"] ?? false,
                        ["is_primary_key"] = reader["is_primary_key"] ?? false,
                        ["pk_ordinal"] = reader["pk_ordinal"] is DBNull ? null : reader["pk_ordinal"],
                        ["default_value"] = reader["default_value"] is DBNull ? null : reader["default_value"],
                        ["computed_definition"] = reader["computed_definition"] is DBNull ? null : reader["computed_definition"],
                        ["column_description"] = reader["column_description"] is DBNull ? null : reader["column_description"]
                    };
                    columns.Add(column);
                }

                reader.Close();

                // Get foreign key information
                var foreignKeysQuery = @"
                    SELECT 
                        fk.name AS constraint_name,
                        c1.name AS column_name,
                        t2.name AS referenced_table,
                        c2.name AS referenced_column
                    FROM sys.foreign_keys fk
                    INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
                    INNER JOIN sys.columns c1 ON fkc.parent_object_id = c1.object_id AND fkc.parent_column_id = c1.column_id
                    INNER JOIN sys.columns c2 ON fkc.referenced_object_id = c2.object_id AND fkc.referenced_column_id = c2.column_id
                    INNER JOIN sys.tables t1 ON fkc.parent_object_id = t1.object_id
                    INNER JOIN sys.tables t2 ON fkc.referenced_object_id = t2.object_id
                    INNER JOIN sys.schemas s1 ON t1.schema_id = s1.schema_id
                    WHERE t1.name = @tableName AND s1.name = @schemaName
                    ORDER BY fk.name, fkc.constraint_column_id";

                var foreignKeys = new List<Dictionary<string, object>>();
                using (var fkCommand = new SqlCommand(foreignKeysQuery, connection))
                {
                    fkCommand.CommandTimeout = _commandTimeout;
                    fkCommand.Parameters.AddWithValue("@tableName", tableName);
                    fkCommand.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");
                    
                    using var fkReader = await fkCommand.ExecuteReaderAsync();
                    while (await fkReader.ReadAsync())
                    {
                        var fk = new Dictionary<string, object>
                        {
                            ["column_name"] = fkReader["column_name"],
                            ["referenced_table"] = fkReader["referenced_table"],
                            ["referenced_column"] = fkReader["referenced_column"],
                            ["constraint_name"] = fkReader["constraint_name"]
                        };
                        foreignKeys.Add(fk);
                    }
                }

                // Get index information per column
                var indexesQuery = @"
                    SELECT 
                        i.name AS index_name,
                        i.type_desc AS index_type,
                        i.is_unique,
                        c.name AS column_name,
                        ic.key_ordinal
                    FROM sys.indexes i
                    INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                    INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                    INNER JOIN sys.tables t ON i.object_id = t.object_id
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    WHERE t.name = @tableName AND s.name = @schemaName AND i.name IS NOT NULL
                    ORDER BY i.name, ic.key_ordinal";

                var indexes = new List<Dictionary<string, object>>();
                using (var idxCommand = new SqlCommand(indexesQuery, connection))
                {
                    idxCommand.CommandTimeout = _commandTimeout;
                    idxCommand.Parameters.AddWithValue("@tableName", tableName);
                    idxCommand.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");
                    
                    using var idxReader = await idxCommand.ExecuteReaderAsync();
                    while (await idxReader.ReadAsync())
                    {
                        var idx = new Dictionary<string, object>
                        {
                            ["index_name"] = idxReader["index_name"],
                            ["index_type"] = idxReader["index_type"],
                            ["is_unique"] = idxReader["is_unique"],
                            ["key_ordinal"] = idxReader["key_ordinal"]
                        };
                        indexes.Add(idx);
                    }
                }

                // Get column statistics if requested
                var columnStats = new List<Dictionary<string, object>>();
                if (includeStatistics)
                {
                    try
                    {
                        var statsQuery = @"
                            SELECT 
                                c.name AS column_name,
                                p.rows AS total_rows,
                                CASE WHEN c.is_nullable = 1 THEN 'NULLABLE' ELSE 'NOT NULL' END AS nullability
                            FROM sys.columns c
                            INNER JOIN sys.tables t ON c.object_id = t.object_id
                            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                            INNER JOIN sys.dm_db_partition_stats p ON t.object_id = p.object_id
                            WHERE t.name = @tableName AND s.name = @schemaName AND p.index_id IN (0,1)";

                        using (var statsCommand = new SqlCommand(statsQuery, connection))
                        {
                            statsCommand.CommandTimeout = _commandTimeout;
                            statsCommand.Parameters.AddWithValue("@tableName", tableName);
                            statsCommand.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");
                            
                            using var statsReader = await statsCommand.ExecuteReaderAsync();
                            while (await statsReader.ReadAsync())
                            {
                                var stat = new Dictionary<string, object>
                                {
                                    ["column_name"] = statsReader["column_name"],
                                    ["total_rows"] = statsReader["total_rows"],
                                    ["nullability"] = statsReader["nullability"]
                                };
                                columnStats.Add(stat);
                            }
                        }
                    }
                    catch
                    {
                        // Statistics might not be available for all columns
                        columnStats.Add(new Dictionary<string, object>
                        {
                            ["error"] = "Statistics not available for some columns"
                        });
                    }
                }

                var schemaData = new
                {
                    table_name = tableName,
                    schema_name = schemaName,
                    column_count = columns.Count,
                    columns = columns,
                    foreign_keys = foreignKeys,
                    indexes_using_column = indexes,
                    column_statistics = includeStatistics ? columnStats : null
                };

                var metadata = new Dictionary<string, object>
                {
                    ["row_count"] = 1,
                    ["page_count"] = 1
                };

                var payload = CreateStandardResponse("GetTableSchema", schemaData, sw.ElapsedMilliseconds, metadata: metadata);
                sw.Stop();
                LogEnd(corr, "GetTableSchema", true, sw.ElapsedMilliseconds);
                return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                LogEnd(Guid.Empty, "GetTableSchema", false, 0, ex.Message);
                var errorPayload = CreateStandardErrorResponse("GetTableSchema", ex.Message);
                return JsonSerializer.Serialize(errorPayload, new JsonSerializerOptions { WriteIndented = true });
            }
        }

        [McpServerTool, Description("Get a list of stored procedures in the current database")]
        public static async Task<string> GetStoredProceduresAsync(
            [Description("Filter by procedure name (partial match, optional)")] string? nameFilter = null)
        {
            try
            {
                var corr = LogStart("GetStoredProcedures", $"name:{nameFilter}");
                var sw = Stopwatch.StartNew();
                using var connection = new SqlConnection(_currentConnectionString);
                await connection.OpenAsync();

                var query = $@"
                    SELECT
                        p.name AS procedure_name,
                        s.name AS schema_name,
                        p.create_date,
                        p.modify_date
                    FROM sys.procedures p
                    INNER JOIN sys.schemas s ON p.schema_id = s.schema_id
                    {(string.IsNullOrEmpty(nameFilter) ? "" : "WHERE p.name LIKE @nameFilter")}
                    ORDER BY s.name, p.name";

                using var command = new SqlCommand(query, connection) { CommandTimeout = _commandTimeout };
                if (!string.IsNullOrEmpty(nameFilter))
                    command.Parameters.AddWithValue("@nameFilter", $"%{nameFilter}%");
                using var reader = await command.ExecuteReaderAsync();

                var procedures = new List<Dictionary<string, object>>();

                while (await reader.ReadAsync())
                {
                    var proc = new Dictionary<string, object>
                    {
                        ["procedure_name"] = reader["procedure_name"],
                        ["schema_name"] = reader["schema_name"],
                        ["create_date"] = reader["create_date"],
                        ["modify_date"] = reader["modify_date"]
                    };
                    procedures.Add(proc);
                }

                var payload = new
                {
                    server_name = _serverName,
                    environment = _environment,
                    database = _currentDatabase,
                    procedure_count = procedures.Count,
                    filters_applied = new
                    {
                        name_filter = nameFilter
                    },
                    procedures = procedures
                };
                sw.Stop();
                LogEnd(corr, "GetStoredProcedures", true, sw.ElapsedMilliseconds);
                return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                LogEnd(Guid.Empty, "GetStoredProcedures", false, 0, ex.Message);
                return JsonSerializer.Serialize(new
                {
                    server_name = _serverName,
                    environment = _environment,
                    database = _currentDatabase,
                    error = ex.Message,
                    operation_type = "ERROR",
                    security_mode = "READ_ONLY_ENFORCED"
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        }

        [McpServerTool, Description("Get detailed information about a specific stored procedure including parameters and definition")]
        public static async Task<string> GetStoredProcedureDetailsAsync(
            [Description("Name of the stored procedure")] string procedureName,
            [Description("Schema name (defaults to 'dbo')")] string? schemaName = "dbo")
        {
            try
            {
                var corr = LogStart("GetStoredProcedureDetails", $"{schemaName}.{procedureName}");
                var sw = Stopwatch.StartNew();
                using var connection = new SqlConnection(_currentConnectionString);
                await connection.OpenAsync();

                // Get stored procedure parameters
                var parametersQuery = @"
                    SELECT
                        p.name AS parameter_name,
                        t.name AS data_type,
                        p.max_length,
                        p.precision,
                        p.scale,
                        p.is_output,
                        p.has_default_value,
                        p.default_value
                    FROM sys.parameters p
                    INNER JOIN sys.types t ON p.user_type_id = t.user_type_id
                    INNER JOIN sys.procedures pr ON p.object_id = pr.object_id
                    INNER JOIN sys.schemas s ON pr.schema_id = s.schema_id
                    WHERE pr.name = @procedureName AND s.name = @schemaName
                    ORDER BY p.parameter_id";

                using var paramsCommand = new SqlCommand(parametersQuery, connection);
                paramsCommand.CommandTimeout = _commandTimeout;
                paramsCommand.Parameters.AddWithValue("@procedureName", procedureName);
                paramsCommand.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                var parameters = new List<Dictionary<string, object>>();
                using (var reader = await paramsCommand.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var param = new Dictionary<string, object>
                        {
                            ["parameter_name"] = reader["parameter_name"] ?? "",
                            ["data_type"] = reader["data_type"] ?? "",
                            ["max_length"] = reader["max_length"] ?? 0,
                            ["precision"] = reader["precision"] ?? 0,
                            ["scale"] = reader["scale"] ?? 0,
                            ["is_output"] = reader["is_output"] ?? false,
                            ["has_default_value"] = reader["has_default_value"] ?? false,
                            ["default_value"] = reader["default_value"] is DBNull ? null : reader["default_value"]
                        };
                        parameters.Add(param);
                    }
                }

                // Get stored procedure definition and metadata
                var definitionQuery = @"
                    SELECT
                        pr.name AS procedure_name,
                        s.name AS schema_name,
                        pr.create_date,
                        pr.modify_date,
                        OBJECT_DEFINITION(pr.object_id) AS definition,
                        pr.is_ms_shipped,
                        pr.is_published,
                        pr.is_schema_published
                    FROM sys.procedures pr
                    INNER JOIN sys.schemas s ON pr.schema_id = s.schema_id
                    WHERE pr.name = @procedureName AND s.name = @schemaName";

                using var defCommand = new SqlCommand(definitionQuery, connection);
                defCommand.CommandTimeout = _commandTimeout;
                defCommand.Parameters.AddWithValue("@procedureName", procedureName);
                defCommand.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                Dictionary<string, object> procedureInfo = null;
                using (var reader = await defCommand.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        procedureInfo = new Dictionary<string, object>
                        {
                            ["procedure_name"] = reader["procedure_name"],
                            ["schema_name"] = reader["schema_name"],
                            ["create_date"] = reader["create_date"],
                            ["modify_date"] = reader["modify_date"],
                            ["definition"] = reader["definition"] is DBNull ? "Definition not available (may be encrypted)" : reader["definition"],
                            ["is_ms_shipped"] = reader["is_ms_shipped"],
                            ["is_published"] = reader["is_published"],
                            ["is_schema_published"] = reader["is_schema_published"]
                        };
                    }
                }

                if (procedureInfo == null)
                {
                    sw.Stop();
                    LogEnd(corr, "GetStoredProcedureDetails", false, sw.ElapsedMilliseconds, "Stored procedure not found");
                    return JsonSerializer.Serialize(new
                    {
                        server_name = _serverName,
                        environment = _environment,
                        database = _currentDatabase,
                        error = $"Stored procedure '{schemaName}.{procedureName}' not found",
                        operation_type = "NOT_FOUND",
                        security_mode = "READ_ONLY_ENFORCED"
                    }, new JsonSerializerOptions { WriteIndented = true });
                }

                // Get dependencies (objects this procedure references)
                var dependenciesQuery = @"
                    SELECT DISTINCT
                        OBJECT_NAME(d.referenced_major_id) AS referenced_object_name,
                        o.type_desc AS object_type
                    FROM sys.sql_dependencies d
                    INNER JOIN sys.procedures pr ON d.object_id = pr.object_id
                    INNER JOIN sys.schemas s ON pr.schema_id = s.schema_id
                    LEFT JOIN sys.objects o ON d.referenced_major_id = o.object_id
                    WHERE pr.name = @procedureName AND s.name = @schemaName
                    ORDER BY o.type_desc, OBJECT_NAME(d.referenced_major_id)";

                using var depsCommand = new SqlCommand(dependenciesQuery, connection);
                depsCommand.CommandTimeout = _commandTimeout;
                depsCommand.Parameters.AddWithValue("@procedureName", procedureName);
                depsCommand.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                var dependencies = new List<Dictionary<string, object>>();
                using (var reader = await depsCommand.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var dep = new Dictionary<string, object>
                        {
                            ["referenced_object_name"] = reader["referenced_object_name"] is DBNull ? null : reader["referenced_object_name"],
                            ["object_type"] = reader["object_type"] is DBNull ? "UNKNOWN" : reader["object_type"]
                        };
                        dependencies.Add(dep);
                    }
                }

                var payload = new
                {
                    server_name = _serverName,
                    environment = _environment,
                    database = _currentDatabase,
                    procedure_info = procedureInfo,
                    parameter_count = parameters.Count,
                    parameters = parameters,
                    dependency_count = dependencies.Count,
                    dependencies = dependencies,
                    security_mode = "READ_ONLY"
                };

                sw.Stop();
                LogEnd(corr, "GetStoredProcedureDetails", true, sw.ElapsedMilliseconds);
                return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                LogEnd(Guid.Empty, "GetStoredProcedureDetails", false, 0, ex.Message);
                return JsonSerializer.Serialize(new
                {
                    server_name = _serverName,
                    environment = _environment,
                    database = _currentDatabase,
                    procedure_name = procedureName,
                    schema_name = schemaName,
                    error = ex.Message,
                    operation_type = "ERROR",
                    security_mode = "READ_ONLY_ENFORCED"
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        }

        [McpServerTool, Description("Get detailed information about any database object (stored procedure, function, or view) including definition, parameters, and dependencies")]
        public static async Task<string> GetObjectDefinitionAsync(
            [Description("Name of the database object (procedure, function, or view)")] string objectName,
            [Description("Schema name (defaults to 'dbo')")] string? schemaName = "dbo",
            [Description("Object type: 'PROCEDURE', 'FUNCTION', 'VIEW', or 'AUTO' to auto-detect (default)")] string? objectType = "AUTO")
        {
            try
            {
                var corr = LogStart("GetObjectDefinition", $"{schemaName}.{objectName}");
                var sw = Stopwatch.StartNew();
                using var connection = new SqlConnection(_currentConnectionString);
                await connection.OpenAsync();

                // Auto-detect object type if needed
                string detectedType = null;
                if (objectType == null || objectType.ToUpperInvariant() == "AUTO")
                {
                    var typeQuery = @"
                        SELECT o.type_desc
                        FROM sys.objects o
                        INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                        WHERE o.name = @objectName AND s.name = @schemaName";

                    using var typeCmd = new SqlCommand(typeQuery, connection);
                    typeCmd.CommandTimeout = _commandTimeout;
                    typeCmd.Parameters.AddWithValue("@objectName", objectName);
                    typeCmd.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                    var result = await typeCmd.ExecuteScalarAsync();
                    detectedType = result?.ToString();

                    if (detectedType == null)
                    {
                        sw.Stop();
                        LogEnd(corr, "GetObjectDefinition", false, sw.ElapsedMilliseconds, "Object not found");
                        return JsonSerializer.Serialize(new
                        {
                            server_name = _serverName,
                            environment = _environment,
                            database = _currentDatabase,
                            error = $"Object '{schemaName}.{objectName}' not found",
                            operation_type = "NOT_FOUND",
                            security_mode = "READ_ONLY_ENFORCED"
                        }, new JsonSerializerOptions { WriteIndented = true });
                    }
                }
                else
                {
                    detectedType = objectType.ToUpperInvariant();
                }

                // Get common object information
                var objectInfoQuery = @"
                    SELECT
                        o.name AS object_name,
                        s.name AS schema_name,
                        o.type_desc AS object_type,
                        o.create_date,
                        o.modify_date,
                        OBJECT_DEFINITION(o.object_id) AS definition
                    FROM sys.objects o
                    INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                    WHERE o.name = @objectName AND s.name = @schemaName";

                using var objCmd = new SqlCommand(objectInfoQuery, connection);
                objCmd.CommandTimeout = _commandTimeout;
                objCmd.Parameters.AddWithValue("@objectName", objectName);
                objCmd.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                Dictionary<string, object> objectInfo = null;
                using (var reader = await objCmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        objectInfo = new Dictionary<string, object>
                        {
                            ["object_name"] = reader["object_name"],
                            ["schema_name"] = reader["schema_name"],
                            ["object_type"] = reader["object_type"],
                            ["create_date"] = reader["create_date"],
                            ["modify_date"] = reader["modify_date"],
                            ["definition"] = reader["definition"] is DBNull ? "Definition not available (may be encrypted)" : reader["definition"]
                        };
                    }
                }

                // Get parameters (for procedures and functions)
                var parameters = new List<Dictionary<string, object>>();
                if (detectedType.Contains("PROCEDURE") || detectedType.Contains("FUNCTION"))
                {
                    var parametersQuery = @"
                        SELECT
                            p.name AS parameter_name,
                            t.name AS data_type,
                            p.max_length,
                            p.precision,
                            p.scale,
                            p.is_output,
                            p.has_default_value,
                            p.default_value
                        FROM sys.parameters p
                        INNER JOIN sys.types t ON p.user_type_id = t.user_type_id
                        INNER JOIN sys.objects o ON p.object_id = o.object_id
                        INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                        WHERE o.name = @objectName AND s.name = @schemaName
                        ORDER BY p.parameter_id";

                    using var paramsCmd = new SqlCommand(parametersQuery, connection);
                    paramsCmd.CommandTimeout = _commandTimeout;
                    paramsCmd.Parameters.AddWithValue("@objectName", objectName);
                    paramsCmd.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                    using (var reader = await paramsCmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var param = new Dictionary<string, object>
                            {
                                ["parameter_name"] = reader["parameter_name"] ?? "",
                                ["data_type"] = reader["data_type"] ?? "",
                                ["max_length"] = reader["max_length"] ?? 0,
                                ["precision"] = reader["precision"] ?? 0,
                                ["scale"] = reader["scale"] ?? 0,
                                ["is_output"] = reader["is_output"] ?? false,
                                ["has_default_value"] = reader["has_default_value"] ?? false,
                                ["default_value"] = reader["default_value"] is DBNull ? null : reader["default_value"]
                            };
                            parameters.Add(param);
                        }
                    }
                }

                // Get columns (for views)
                var columns = new List<Dictionary<string, object>>();
                if (detectedType.Contains("VIEW"))
                {
                    var columnsQuery = @"
                        SELECT
                            c.name AS column_name,
                            t.name AS data_type,
                            c.max_length,
                            c.precision,
                            c.scale,
                            c.is_nullable,
                            c.is_identity,
                            c.is_computed
                        FROM sys.columns c
                        INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
                        INNER JOIN sys.views v ON c.object_id = v.object_id
                        INNER JOIN sys.schemas s ON v.schema_id = s.schema_id
                        WHERE v.name = @objectName AND s.name = @schemaName
                        ORDER BY c.column_id";

                    using var colsCmd = new SqlCommand(columnsQuery, connection);
                    colsCmd.CommandTimeout = _commandTimeout;
                    colsCmd.Parameters.AddWithValue("@objectName", objectName);
                    colsCmd.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                    using (var reader = await colsCmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var col = new Dictionary<string, object>
                            {
                                ["column_name"] = reader["column_name"] ?? "",
                                ["data_type"] = reader["data_type"] ?? "",
                                ["max_length"] = reader["max_length"] ?? 0,
                                ["precision"] = reader["precision"] ?? 0,
                                ["scale"] = reader["scale"] ?? 0,
                                ["is_nullable"] = reader["is_nullable"] ?? false,
                                ["is_identity"] = reader["is_identity"] ?? false,
                                ["is_computed"] = reader["is_computed"] ?? false
                            };
                            columns.Add(col);
                        }
                    }
                }

                // Get dependencies
                var dependencies = new List<Dictionary<string, object>>();
                var dependenciesQuery = @"
                    SELECT DISTINCT
                        OBJECT_NAME(d.referenced_major_id) AS referenced_object_name,
                        o.type_desc AS object_type
                    FROM sys.sql_dependencies d
                    INNER JOIN sys.objects obj ON d.object_id = obj.object_id
                    INNER JOIN sys.schemas s ON obj.schema_id = s.schema_id
                    LEFT JOIN sys.objects o ON d.referenced_major_id = o.object_id
                    WHERE obj.name = @objectName AND s.name = @schemaName
                    ORDER BY o.type_desc, OBJECT_NAME(d.referenced_major_id)";

                using var depsCmd = new SqlCommand(dependenciesQuery, connection);
                depsCmd.CommandTimeout = _commandTimeout;
                depsCmd.Parameters.AddWithValue("@objectName", objectName);
                depsCmd.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                using (var reader = await depsCmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var dep = new Dictionary<string, object>
                        {
                            ["referenced_object_name"] = reader["referenced_object_name"] is DBNull ? null : reader["referenced_object_name"],
                            ["object_type"] = reader["object_type"] is DBNull ? "UNKNOWN" : reader["object_type"]
                        };
                        dependencies.Add(dep);
                    }
                }

                // Build response based on object type
                var payload = new Dictionary<string, object>
                {
                    ["server_name"] = _serverName,
                    ["environment"] = _environment,
                    ["database"] = _currentDatabase,
                    ["object_info"] = objectInfo,
                    ["dependency_count"] = dependencies.Count,
                    ["dependencies"] = dependencies,
                    ["security_mode"] = "READ_ONLY"
                };

                if (detectedType.Contains("PROCEDURE") || detectedType.Contains("FUNCTION"))
                {
                    payload["parameter_count"] = parameters.Count;
                    payload["parameters"] = parameters;
                }

                if (detectedType.Contains("VIEW"))
                {
                    payload["column_count"] = columns.Count;
                    payload["columns"] = columns;
                }

                sw.Stop();
                LogEnd(corr, "GetObjectDefinition", true, sw.ElapsedMilliseconds);
                return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                LogEnd(Guid.Empty, "GetObjectDefinition", false, 0, ex.Message);
                return JsonSerializer.Serialize(new
                {
                    server_name = _serverName,
                    environment = _environment,
                    database = _currentDatabase,
                    object_name = objectName,
                    schema_name = schemaName,
                    error = ex.Message,
                    operation_type = "ERROR",
                    security_mode = "READ_ONLY_ENFORCED"
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        }
    }
}
