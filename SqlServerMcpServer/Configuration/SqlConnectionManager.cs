using Microsoft.Data.SqlClient;
using System.Text.Json;
using System.IO;
using System.Linq;
using System;

namespace SqlServerMcpServer.Configuration
{
    /// <summary>
    /// Manages SQL Server connection strings and configuration
    /// </summary>
    public static class SqlConnectionManager
    {
        private static string _currentConnectionString =
            System.Environment.GetEnvironmentVariable("SQLSERVER_CONNECTION_STRING")
            ?? GetConfigValue("SqlServer", "ConnectionString")
            ?? "Server=localhost,14333;Database=master;User Id=sa;Password=Your_Str0ng_Pass!;TrustServerCertificate=true;";

        private static string _currentDatabase = GetDatabaseFromConnectionString(_currentConnectionString);
        private static string _serverName = System.Environment.GetEnvironmentVariable("MCP_SERVER_NAME") ?? "SQL Server MCP";
        private static string _environment = System.Environment.GetEnvironmentVariable("MCP_ENVIRONMENT") ?? "unknown";
        private static int _commandTimeout = ParseIntEnv("SQLSERVER_COMMAND_TIMEOUT",
            ParseIntConfig("SqlServer", "CommandTimeout", 30));

        static SqlConnectionManager()
        {
            Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] SqlConnectionManager static constructor called");
            Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] Initializing with database: {_currentDatabase}");
        }

        /// <summary>
        /// Gets the current connection string
        /// </summary>
        public static string CurrentConnectionString => _currentConnectionString;

        /// <summary>
        /// Gets the current database name
        /// </summary>
        public static string CurrentDatabase => _currentDatabase;

        /// <summary>
        /// Gets the server name
        /// </summary>
        public static string ServerName => _serverName;

        /// <summary>
        /// Gets the environment name
        /// </summary>
        public static string Environment => _environment;

        /// <summary>
        /// Gets the command timeout in seconds
        /// </summary>
        public static int CommandTimeout => _commandTimeout;

        /// <summary>
        /// Creates a new SQL connection with the current connection string
        /// </summary>
        /// <returns>A new SqlConnection instance</returns>
        public static SqlConnection CreateConnection()
        {
            return new SqlConnection(_currentConnectionString);
        }

        /// <summary>
        /// Creates a connection string for a specific database
        /// </summary>
        /// <param name="databaseName">The database name</param>
        /// <returns>Connection string for the specified database</returns>
        public static string CreateConnectionStringForDatabase(string databaseName)
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

        /// <summary>
        /// Switches to a different database
        /// </summary>
        /// <param name="databaseName">The database name to switch to</param>
        /// <exception cref="Exception">Thrown when unable to connect to the specified database</exception>
        public static void SwitchDatabase(string databaseName)
        {
            // Test connection to the new database first
            var testConnectionString = CreateConnectionStringForDatabase(databaseName);
            using var testConnection = new SqlConnection(testConnectionString);
            testConnection.Open();

            _currentConnectionString = testConnectionString;
            _currentDatabase = databaseName;
        }

        /// <summary>
        /// Parses an integer from environment variable
        /// </summary>
        /// <param name="name">Environment variable name</param>
        /// <param name="defaultValue">Default value if parsing fails</param>
        /// <returns>Parsed integer or default value</returns>
        private static int ParseIntEnv(string name, int defaultValue)
        {
            var val = System.Environment.GetEnvironmentVariable(name);
            return int.TryParse(val, out var parsed) && parsed > 0 ? parsed : defaultValue;
        }

        /// <summary>
        /// Gets a configuration value from appsettings.json
        /// </summary>
        /// <param name="section">Configuration section</param>
        /// <param name="key">Configuration key</param>
        /// <returns>Configuration value or null if not found</returns>
        private static string? GetConfigValue(string section, string key)
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var currentDir = Directory.GetCurrentDirectory();
                var parentDir = Directory.GetParent(currentDir)?.FullName;
                var candidatePaths = new[]
                {
                    Path.Combine(baseDir, "appsettings.json"),
                    Path.Combine(currentDir, "SqlServerMcpServer", "appsettings.json"),
                    Path.Combine(parentDir ?? string.Empty, "SqlServerMcpServer", "appsettings.json"),
                    Path.Combine(currentDir, "appsettings.json"),
                    Path.Combine(parentDir ?? string.Empty, "appsettings.json")
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

        /// <summary>
        /// Parses an integer from configuration
        /// </summary>
        /// <param name="section">Configuration section</param>
        /// <param name="key">Configuration key</param>
        /// <param name="defaultValue">Default value if parsing fails</param>
        /// <returns>Parsed integer or default value</returns>
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

        /// <summary>
        /// Extracts database name from connection string
        /// </summary>
        /// <param name="connectionString">The connection string</param>
        /// <returns>Database name or "master" if not found</returns>
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
    }
}