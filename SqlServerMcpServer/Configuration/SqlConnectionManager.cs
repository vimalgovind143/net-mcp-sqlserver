using Microsoft.Data.SqlClient;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SqlServerMcpServer.Configuration
{
    /// <summary>
    /// Manages a registry of named SQL Server connections.
    /// A "default" connection is always auto-created from SQLSERVER_CONNECTION_STRING / appsettings.json.
    /// Additional named connections can be added via SQLSERVER_CONN_&lt;NAME&gt; environment variables,
    /// the NamedConnections section in appsettings.json, or at runtime via AddConnection().
    /// </summary>
    public static class SqlConnectionManager
    {
        // ── registry ────────────────────────────────────────────────────────────
        private static readonly ConcurrentDictionary<string, ConnectionInfo> _connections =
            new(StringComparer.OrdinalIgnoreCase);

        private static volatile string _activeConnectionName = "default";

        // ── scalar config ────────────────────────────────────────────────────────
        private static readonly string _serverName =
            System.Environment.GetEnvironmentVariable("MCP_SERVER_NAME") ?? "SQL Server MCP";

        private static readonly string _environment =
            System.Environment.GetEnvironmentVariable("MCP_ENVIRONMENT") ?? "unknown";

        private static readonly int _commandTimeout = ParseIntEnv(
            "SQLSERVER_COMMAND_TIMEOUT",
            ParseIntConfig("SqlServer", "CommandTimeout", 30));

        // ── bootstrap ─────────────────────────────────────────────────────────
        static SqlConnectionManager()
        {
            Log("[INFO] SqlConnectionManager initializing");

            // 1. Default connection from env / appsettings / built-in fallback
            var defaultConnStr =
                System.Environment.GetEnvironmentVariable("SQLSERVER_CONNECTION_STRING")
                ?? GetConfigValue("SqlServer", "ConnectionString")
                ?? "Server=localhost,14333;Database=master;User Id=sa;Password=Your_Str0ng_Pass!;TrustServerCertificate=true;";

            RegisterConnection("default", defaultConnStr, isActive: true);

            // 2. Named connections from SQLSERVER_CONN_<NAME> environment variables
            foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
            {
                var key = entry.Key?.ToString() ?? string.Empty;
                if (!key.StartsWith("SQLSERVER_CONN_", StringComparison.OrdinalIgnoreCase)) continue;

                var name = key["SQLSERVER_CONN_".Length..].ToLowerInvariant();
                var connStr = entry.Value?.ToString();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(connStr)) continue;

                RegisterConnection(name, connStr, isActive: false);
                Log($"[INFO] Loaded named connection '{name}' from env var {key}");
            }

            // 3. Named connections from appsettings.json NamedConnections section
            LoadNamedConnectionsFromConfig();

            Log($"[INFO] SqlConnectionManager ready. Active='{_activeConnectionName}', total={_connections.Count}");
        }

        // ── public properties (backward-compat) ──────────────────────────────
        public static string CurrentConnectionString => GetActive().ConnectionString;
        public static string CurrentDatabase         => GetActive().CurrentDatabase;
        public static string ServerName              => _serverName;
        public static string Environment             => _environment;
        public static int    CommandTimeout          => _commandTimeout;

        // ── connection management ────────────────────────────────────────────

        /// <summary>
        /// Adds or replaces a named connection. Optionally tests connectivity before registering.
        /// Throws <see cref="InvalidOperationException"/> if testConnection=true and the connection fails.
        /// </summary>
        public static void AddConnection(string name, string connectionString, bool testConnection = true, bool setAsActive = false)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Connection name cannot be empty.", nameof(name));
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string cannot be empty.", nameof(connectionString));

            if (testConnection)
            {
                using var testConn = new SqlConnection(connectionString);
                testConn.Open(); // throws SqlException on failure
            }

            RegisterConnection(name, connectionString, isActive: false);

            if (setAsActive)
                SwitchConnection(name);

            Log($"[INFO] AddConnection: '{name}' registered (setAsActive={setAsActive})");
        }

        /// <summary>
        /// Removes a named connection from the registry.
        /// Returns false if the connection does not exist or is the last remaining connection.
        /// If the removed connection was active, switches to "default".
        /// </summary>
        public static bool RemoveConnection(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (name.Equals("default", StringComparison.OrdinalIgnoreCase)) return false; // default is permanent
            if (!_connections.TryRemove(name, out _)) return false;

            if (_activeConnectionName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                _activeConnectionName = "default";
                if (_connections.TryGetValue("default", out var def))
                    def.IsActive = true;
            }

            Log($"[INFO] RemoveConnection: '{name}' removed");
            return true;
        }

        /// <summary>Switches the active connection to the named connection.</summary>
        /// <exception cref="KeyNotFoundException">Thrown when the connection name is not registered.</exception>
        public static void SwitchConnection(string name)
        {
            if (!_connections.TryGetValue(name, out var target))
                throw new KeyNotFoundException($"Connection '{name}' not found. Available: {string.Join(", ", GetConnectionNames())}");

            // Deactivate all, activate target
            foreach (var c in _connections.Values)
                c.IsActive = false;

            target.IsActive = true;
            _activeConnectionName = target.Name;
            Log($"[INFO] SwitchConnection: active connection is now '{target.Name}'");
        }

        /// <summary>Returns the name of the currently active connection.</summary>
        public static string GetActiveConnectionName() => _activeConnectionName;

        /// <summary>Returns all registered connection names.</summary>
        public static IReadOnlyCollection<string> GetConnectionNames() =>
            _connections.Keys.ToList().AsReadOnly();

        /// <summary>Returns all registered connections as a read-only snapshot.</summary>
        public static IReadOnlyCollection<ConnectionInfo> GetAllConnections() =>
            _connections.Values.ToList().AsReadOnly();

        /// <summary>Returns true if a connection with the given name is registered.</summary>
        public static bool ConnectionExists(string name) =>
            !string.IsNullOrWhiteSpace(name) && _connections.ContainsKey(name);

        /// <summary>
        /// Returns the <see cref="ConnectionInfo"/> for the given name,
        /// or the active connection when name is null/empty.
        /// </summary>
        public static ConnectionInfo GetConnection(string? name = null) =>
            string.IsNullOrWhiteSpace(name) ? GetActive() : Get(name);

        /// <summary>
        /// Creates a new <see cref="SqlConnection"/> for the given named connection,
        /// or the active connection when name is null/empty.
        /// Updates <see cref="ConnectionInfo.LastUsed"/>.
        /// </summary>
        public static SqlConnection CreateConnection(string? name = null)
        {
            var info = GetConnection(name);
            info.LastUsed = DateTime.UtcNow;
            return new SqlConnection(info.ConnectionString);
        }

        // ── database switching (operates on active connection) ────────────────

        /// <summary>
        /// Creates a connection string pointing at a different database on the ACTIVE server.
        /// Returns the original if databaseName is null/empty.
        /// </summary>
        public static string CreateConnectionStringForDatabase(string? databaseName)
        {
            if (string.IsNullOrWhiteSpace(databaseName))
                return CurrentConnectionString;

            try
            {
                var builder = new SqlConnectionStringBuilder(CurrentConnectionString)
                {
                    InitialCatalog = databaseName
                };
                return builder.ConnectionString;
            }
            catch
            {
                return CurrentConnectionString;
            }
        }

        /// <summary>
        /// Switches the active connection to a different database on the SAME server.
        /// Mutates the active <see cref="ConnectionInfo.CurrentDatabase"/> and
        /// <see cref="ConnectionInfo.ConnectionString"/> in-place.
        /// </summary>
        public static void SwitchDatabase(string databaseName)
        {
            var newConnStr = CreateConnectionStringForDatabase(databaseName);
            using var test = new SqlConnection(newConnStr);
            test.Open();

            var active = GetActive();
            active.ConnectionString = newConnStr;
            active.CurrentDatabase  = databaseName;
        }

        // ── internal helpers ─────────────────────────────────────────────────

        private static ConnectionInfo GetActive()
        {
            if (_connections.TryGetValue(_activeConnectionName, out var active))
                return active;

            // Fallback: first available
            var first = _connections.Values.FirstOrDefault();
            if (first is not null) return first;

            throw new InvalidOperationException("No SQL Server connections are registered.");
        }

        private static ConnectionInfo Get(string name)
        {
            if (_connections.TryGetValue(name, out var info))
                return info;

            throw new KeyNotFoundException(
                $"Connection '{name}' not found. Available: {string.Join(", ", GetConnectionNames())}");
        }

        private static void RegisterConnection(string name, string connectionString, bool isActive)
        {
            var info = new ConnectionInfo
            {
                Name             = name,
                ConnectionString = connectionString,
                ServerName       = GetServerFromConnectionString(connectionString),
                CurrentDatabase  = GetDatabaseFromConnectionString(connectionString),
                CreatedAt        = DateTime.UtcNow,
                IsActive         = isActive
            };
            _connections[name] = info;

            if (isActive)
                _activeConnectionName = name;
        }

        private static void LoadNamedConnectionsFromConfig()
        {
            try
            {
                var baseDir     = AppContext.BaseDirectory;
                var currentDir  = Directory.GetCurrentDirectory();
                var parentDir   = Directory.GetParent(currentDir)?.FullName;
                var candidates  = new[]
                {
                    Path.Combine(baseDir,    "appsettings.json"),
                    Path.Combine(currentDir, "SqlServerMcpServer", "appsettings.json"),
                    Path.Combine(parentDir ?? string.Empty, "SqlServerMcpServer", "appsettings.json"),
                    Path.Combine(currentDir, "appsettings.json"),
                    Path.Combine(parentDir ?? string.Empty, "appsettings.json")
                };

                var path = candidates.FirstOrDefault(File.Exists);
                if (path is null) return;

                using var stream = File.OpenRead(path);
                using var doc    = JsonDocument.Parse(stream);

                if (!doc.RootElement.TryGetProperty("SqlServer", out var sqlSection)) return;
                if (!sqlSection.TryGetProperty("NamedConnections", out var namedSection)) return;

                // Support object { "prod": "Server=...", "reporting": "Server=..." }
                if (namedSection.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in namedSection.EnumerateObject())
                    {
                        var name    = prop.Name.ToLowerInvariant();
                        var connStr = prop.Value.GetString();
                        if (string.IsNullOrWhiteSpace(connStr)) continue;

                        // Env vars take precedence — don't overwrite
                        if (!_connections.ContainsKey(name))
                        {
                            RegisterConnection(name, connStr, isActive: false);
                            Log($"[INFO] Loaded named connection '{name}' from appsettings.json");
                        }
                    }
                }
            }
            catch { /* appsettings is optional */ }
        }

        private static string GetDatabaseFromConnectionString(string connectionString)
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                return string.IsNullOrWhiteSpace(builder.InitialCatalog) ? "master" : builder.InitialCatalog;
            }
            catch { return "master"; }
        }

        private static string GetServerFromConnectionString(string connectionString)
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                return builder.DataSource ?? "unknown";
            }
            catch { return "unknown"; }
        }

        private static string? GetConfigValue(string section, string key)
        {
            try
            {
                var baseDir    = AppContext.BaseDirectory;
                var currentDir = Directory.GetCurrentDirectory();
                var parentDir  = Directory.GetParent(currentDir)?.FullName;
                var candidates = new[]
                {
                    Path.Combine(baseDir,    "appsettings.json"),
                    Path.Combine(currentDir, "SqlServerMcpServer", "appsettings.json"),
                    Path.Combine(parentDir ?? string.Empty, "SqlServerMcpServer", "appsettings.json"),
                    Path.Combine(currentDir, "appsettings.json"),
                    Path.Combine(parentDir ?? string.Empty, "appsettings.json")
                };

                var path = candidates.FirstOrDefault(File.Exists);
                if (path is null) return null;

                using var stream = File.OpenRead(path);
                using var doc    = JsonDocument.Parse(stream);
                if (doc.RootElement.TryGetProperty(section, out var sec) &&
                    sec.TryGetProperty(key, out var val) &&
                    val.ValueKind == JsonValueKind.String)
                    return val.GetString();
            }
            catch { }
            return null;
        }

        private static int ParseIntEnv(string name, int defaultValue)
        {
            var val = System.Environment.GetEnvironmentVariable(name);
            return int.TryParse(val, out var parsed) && parsed > 0 ? parsed : defaultValue;
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

        private static void Log(string message) =>
            Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
    }
}
