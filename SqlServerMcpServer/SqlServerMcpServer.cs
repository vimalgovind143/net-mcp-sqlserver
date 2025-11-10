using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        [McpServerTool, Description("Get a list of all databases on the SQL Server instance")]
        public static async Task<string> GetDatabasesAsync()
        {
            try
            {
                var corr = LogStart("GetDatabases");
                var sw = Stopwatch.StartNew();
                // Use master database connection for listing databases
                var masterConnectionString = CreateConnectionStringForDatabase("master");
                using var connection = new SqlConnection(masterConnectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT
                        name AS database_name,
                        database_id,
                        create_date,
                        state_desc,
                        CASE WHEN name = @CurrentDb THEN 1 ELSE 0 END AS is_current
                    FROM sys.databases
                    WHERE name NOT IN ('master', 'tempdb', 'model', 'msdb')
                    ORDER BY name";

                using var command = new SqlCommand(query, connection);
                command.CommandTimeout = _commandTimeout;
                command.Parameters.AddWithValue("@CurrentDb", _currentDatabase);

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
                        ["is_current"] = reader["is_current"]
                    };
                    databases.Add(database);
                }

                var payload = databases;
                sw.Stop();
                LogEnd(corr, "GetDatabases", true, sw.ElapsedMilliseconds);
                return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                LogEnd(Guid.Empty, "GetDatabases", false, 0, ex.Message);
                return JsonSerializer.Serialize(new
                {
                    server_name = _serverName,
                    environment = _environment,
                    database = _currentDatabase,
                    error = $"Error getting databases: {ex.Message}",
                    operation_type = "ERROR",
                    security_mode = "READ_ONLY_ENFORCED"
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        }

        [McpServerTool, Description("Execute a read-only SQL query on the current database")]
        public static async Task<string> ExecuteQueryAsync(
            [Description("The SQL query to execute (SELECT statements only)")] string query,
            [Description("Maximum rows to return (default 100, capped at 100)")] int? maxRows = 100)
        {
            try
            {
                var corr = LogStart("ExecuteQuery", query);
                var sw = Stopwatch.StartNew();
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
                        help = "This MCP server is configured for READ-ONLY access to prevent accidental data modification. Use SELECT statements to query data."
                    }, new JsonSerializerOptions { WriteIndented = true });
                }

                using var connection = new SqlConnection(_currentConnectionString);
                await connection.OpenAsync();

                // Set read-only intent for additional safety
                var enforcedLimit = Math.Min(maxRows ?? 100, 100);
                var finalQuery = ApplyTopLimit(query, enforcedLimit);
                using var command = new SqlCommand(finalQuery, connection)
                {
                    CommandTimeout = _commandTimeout // Prevent long-running queries
                };

                using var reader = await command.ExecuteReaderAsync();

                var results = new List<Dictionary<string, object>>();

                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var columnName = reader.GetName(i);
                        var value = reader.GetValue(i);
                        row[columnName] = value is DBNull ? null : value;
                    }
                    results.Add(row);
                }

                var payload = new
                {
                    server_name = _serverName,
                    environment = _environment,
                    database = _currentDatabase,
                    row_count = results.Count,
                    operation_type = "READ_ONLY_SELECT",
                    security_mode = "READ_ONLY_ENFORCED",
                    message = "✅ Read-only SELECT query executed successfully",
                    data = results,
                    applied_limit = enforcedLimit
                };
                sw.Stop();
                LogEnd(corr, "ExecuteQuery", true, sw.ElapsedMilliseconds);
                return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                LogEnd(Guid.Empty, "ExecuteQuery", false, 0, ex.Message);
                return JsonSerializer.Serialize(new
                {
                    server_name = _serverName,
                    environment = _environment,
                    database = _currentDatabase,
                    error = $"SQL Error: {ex.Message}",
                    operation_type = "ERROR",
                    security_mode = "READ_ONLY_ENFORCED",
                    help = "Check your SQL syntax and ensure you're only using SELECT statements.",
                    applied_limit = Math.Min(maxRows ?? 100, 100)
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        }

        private static string ApplyTopLimit(string query, int maxRows)
        {
            try
            {
                // Do not alter if TOP already present near first SELECT
                var withoutComments = Regex.Replace(query, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
                withoutComments = Regex.Replace(withoutComments, @"--.*?$", string.Empty, RegexOptions.Multiline);

                // If TOP exists, cap it to maxRows
                var topMatch = Regex.Match(withoutComments, @"\bSELECT\s+(DISTINCT\s+)?TOP\s+(\d+)\b", RegexOptions.IgnoreCase);
                if (topMatch.Success)
                {
                    if (int.TryParse(topMatch.Groups[2].Value, out var existing))
                    {
                        var newTop = Math.Min(existing, maxRows);
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
                        return query; // Existing TOP already within cap
                    }
                    return query; // Non-numeric TOP, leave unchanged
                }

                // No TOP: insert TOP maxRows after first SELECT
                var replaced = Regex.Replace(
                    withoutComments,
                    @"\bSELECT\s+(DISTINCT\s+)?",
                    m =>
                    {
                        var distinct = m.Groups[1].Value;
                        return $"SELECT {distinct}TOP {maxRows} ";
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

        [McpServerTool, Description("Get a list of all tables in the current database")]
        public static async Task<string> GetTablesAsync()
        {
            try
            {
                var corr = LogStart("GetTables");
                var sw = Stopwatch.StartNew();
                using var connection = new SqlConnection(_currentConnectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT
                        t.name AS table_name,
                        s.name AS schema_name,
                        p.rows AS row_count
                    FROM sys.tables t
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    LEFT JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0,1)
                    GROUP BY t.name, s.name, p.rows
                    ORDER BY s.name, t.name";

                using var command = new SqlCommand(query, connection);
                command.CommandTimeout = _commandTimeout;
                using var reader = await command.ExecuteReaderAsync();

                var tables = new List<Dictionary<string, object>>();

                while (await reader.ReadAsync())
                {
                    var table = new Dictionary<string, object>
                    {
                        ["table_name"] = reader["table_name"],
                        ["schema_name"] = reader["schema_name"],
                        ["row_count"] = reader["row_count"]
                    };
                    tables.Add(table);
                }

                var payload = new
                {
                    database = _currentDatabase,
                    table_count = tables.Count,
                    tables = tables
                };
                sw.Stop();
                LogEnd(corr, "GetTables", true, sw.ElapsedMilliseconds);
                return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                LogEnd(Guid.Empty, "GetTables", false, 0, ex.Message);
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

        [McpServerTool, Description("Get the schema information for a specific table")]
        public static async Task<string> GetTableSchemaAsync(
            [Description("Name of the table")] string tableName,
            [Description("Schema name (defaults to 'dbo')")] string? schemaName = "dbo")
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
                        c.is_nullable,
                        c.is_identity
                    FROM sys.columns c
                    INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
                    INNER JOIN sys.tables tbl ON c.object_id = tbl.object_id
                    INNER JOIN sys.schemas s ON tbl.schema_id = s.schema_id
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
                        ["column_name"] = reader["column_name"],
                        ["data_type"] = reader["data_type"],
                        ["max_length"] = reader["max_length"],
                        ["is_nullable"] = reader["is_nullable"],
                        ["is_identity"] = reader["is_identity"]
                    };
                    columns.Add(column);
                }

                var payload = new
                {
                    database = _currentDatabase,
                    table_name = tableName,
                    schema_name = schemaName,
                    column_count = columns.Count,
                    columns = columns
                };
                sw.Stop();
                LogEnd(corr, "GetTableSchema", true, sw.ElapsedMilliseconds);
                return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                LogEnd(Guid.Empty, "GetTableSchema", false, 0, ex.Message);
                return JsonSerializer.Serialize(new
                {
                    server_name = _serverName,
                    environment = _environment,
                    database = _currentDatabase,
                    table_name = tableName,
                    schema_name = schemaName,
                    error = ex.Message,
                    operation_type = "ERROR",
                    security_mode = "READ_ONLY_ENFORCED"
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        }

        [McpServerTool, Description("Get a list of stored procedures in the current database")]
        public static async Task<string> GetStoredProceduresAsync()
        {
            try
            {
                var corr = LogStart("GetStoredProcedures");
                var sw = Stopwatch.StartNew();
                using var connection = new SqlConnection(_currentConnectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT
                        p.name AS procedure_name,
                        s.name AS schema_name,
                        p.create_date,
                        p.modify_date
                    FROM sys.procedures p
                    INNER JOIN sys.schemas s ON p.schema_id = s.schema_id
                    ORDER BY s.name, p.name";

                using var command = new SqlCommand(query, connection) { CommandTimeout = _commandTimeout };
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
                            ["parameter_name"] = reader["parameter_name"],
                            ["data_type"] = reader["data_type"],
                            ["max_length"] = reader["max_length"],
                            ["precision"] = reader["precision"],
                            ["scale"] = reader["scale"],
                            ["is_output"] = reader["is_output"],
                            ["has_default_value"] = reader["has_default_value"],
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
                                ["parameter_name"] = reader["parameter_name"],
                                ["data_type"] = reader["data_type"],
                                ["max_length"] = reader["max_length"],
                                ["precision"] = reader["precision"],
                                ["scale"] = reader["scale"],
                                ["is_output"] = reader["is_output"],
                                ["has_default_value"] = reader["has_default_value"],
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
                                ["column_name"] = reader["column_name"],
                                ["data_type"] = reader["data_type"],
                                ["max_length"] = reader["max_length"],
                                ["precision"] = reader["precision"],
                                ["scale"] = reader["scale"],
                                ["is_nullable"] = reader["is_nullable"],
                                ["is_identity"] = reader["is_identity"],
                                ["is_computed"] = reader["is_computed"]
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
