using System.Text.Json;
using SqlServerMcpServer.Configuration;

namespace SqlServerMcpServer.Utilities
{
    /// <summary>
    /// Provides audit logging for data modification operations with log rotation
    /// </summary>
    public static class AuditService
    {
        private static readonly object _lockObject = new object();
        private static string? _auditLogDir;
        private static string? _currentLogFile;
        private static DateTime _currentLogDate;
        private static long _currentFileSize;
        private static bool _initialized = false;

        // Configuration defaults
        private static readonly long _maxFileSizeBytes = ParseLongEnv("MCP_AUDIT_MAX_FILE_SIZE_MB", 100) * 1024 * 1024; // Default 100MB
        private static readonly int _retentionDays = ParseIntEnv("MCP_AUDIT_RETENTION_DAYS", 30); // Default 30 days
        private static readonly int _maxFilesPerDay = ParseIntEnv("MCP_AUDIT_MAX_FILES_PER_DAY", 10); // Default 10 files per day

        /// <summary>
        /// Represents an audit log entry for a DML operation
        /// </summary>
        public class AuditEntry
        {
            public string Timestamp { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            public string Operation { get; set; } = string.Empty;
            public string Database { get; set; } = string.Empty;
            public string Query { get; set; } = string.Empty;
            public int RowsAffected { get; set; }
            public bool HadConfirmation { get; set; }
            public Dictionary<string, object>? Parameters { get; set; }
            public long ExecutionTimeMs { get; set; }
            public string? CorrelationId { get; set; }
            public string ServerName { get; set; } = string.Empty;
            public string Environment { get; set; } = string.Empty;
        }

        /// <summary>
        /// Audit log rotation configuration
        /// </summary>
        public class RotationConfig
        {
            public long MaxFileSizeBytes { get; set; }
            public int RetentionDays { get; set; }
            public int MaxFilesPerDay { get; set; }
            public string LogDirectory { get; set; } = string.Empty;
        }

        /// <summary>
        /// Gets the current rotation configuration
        /// </summary>
        public static RotationConfig GetRotationConfig()
        {
            Initialize();
            return new RotationConfig
            {
                MaxFileSizeBytes = _maxFileSizeBytes,
                RetentionDays = _retentionDays,
                MaxFilesPerDay = _maxFilesPerDay,
                LogDirectory = _auditLogDir ?? "not configured"
            };
        }

        /// <summary>
        /// Initializes the audit service with the configured log path
        /// </summary>
        private static void Initialize()
        {
            if (_initialized) return;

            lock (_lockObject)
            {
                if (_initialized) return;

                // Get audit log directory from environment or use default
                _auditLogDir = Environment.GetEnvironmentVariable("MCP_AUDIT_LOG_PATH")
                    ?? Path.Combine(AppContext.BaseDirectory, "logs", "audit");

                try
                {
                    Directory.CreateDirectory(_auditLogDir);
                    _currentLogDate = DateTime.UtcNow.Date;
                    _currentLogFile = GetLogFilePathForDate(_currentLogDate);
                    _currentFileSize = GetFileSize(_currentLogFile);
                    _initialized = true;

                    // Clean up old logs on initialization
                    CleanupOldLogs();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ERROR] Failed to initialize audit logging: {ex.Message}");
                    _auditLogDir = null;
                    _currentLogFile = null;
                    _initialized = true;
                }
            }
        }

        /// <summary>
        /// Gets the log file path for a specific date, handling rotation
        /// </summary>
        private static string GetLogFilePathForDate(DateTime date)
        {
            if (_auditLogDir == null) return string.Empty;

            var baseFileName = $"dml-audit-{date:yyyy-MM-dd}";
            var basePath = Path.Combine(_auditLogDir, baseFileName + ".log");

            // If base file doesn't exist or is under size limit, use it
            if (!File.Exists(basePath) || new FileInfo(basePath).Length < _maxFileSizeBytes)
            {
                return basePath;
            }

            // Find next available rotated file
            for (int i = 1; i <= _maxFilesPerDay; i++)
            {
                var rotatedPath = Path.Combine(_auditLogDir, $"{baseFileName}.{i:000}.log");
                if (!File.Exists(rotatedPath) || new FileInfo(rotatedPath).Length < _maxFileSizeBytes)
                {
                    return rotatedPath;
                }
            }

            // If all rotation slots are full, use the last one (will be overwritten)
            return Path.Combine(_auditLogDir, $"{baseFileName}.{_maxFilesPerDay:000}.log");
        }

        /// <summary>
        /// Gets the size of a file, or 0 if it doesn't exist
        /// </summary>
        private static long GetFileSize(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return 0;
            return new FileInfo(path).Length;
        }

        /// <summary>
        /// Checks if we need to rotate to a new file (new day or size limit)
        /// </summary>
        private static void CheckRotation()
        {
            if (_auditLogDir == null) return;

            var today = DateTime.UtcNow.Date;

            // Check for new day
            if (today != _currentLogDate)
            {
                _currentLogDate = today;
                _currentLogFile = GetLogFilePathForDate(_currentLogDate);
                _currentFileSize = GetFileSize(_currentLogFile);
                CleanupOldLogs();
                return;
            }

            // Check for size limit
            if (_currentFileSize >= _maxFileSizeBytes)
            {
                _currentLogFile = GetLogFilePathForDate(_currentLogDate);
                _currentFileSize = GetFileSize(_currentLogFile);
            }
        }

        /// <summary>
        /// Cleans up audit logs older than retention period
        /// </summary>
        private static void CleanupOldLogs()
        {
            if (_auditLogDir == null || !Directory.Exists(_auditLogDir)) return;

            try
            {
                var cutoffDate = DateTime.UtcNow.Date.AddDays(-_retentionDays);
                var files = Directory.GetFiles(_auditLogDir, "dml-audit-*.log");

                foreach (var file in files)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.LastWriteTimeUtc < cutoffDate)
                        {
                            fileInfo.Delete();
                            Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] Deleted old audit log: {fileInfo.Name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [WARN] Failed to delete old audit log {file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [WARN] Failed to cleanup old audit logs: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the current audit log file path
        /// </summary>
        public static string? GetAuditLogPath()
        {
            Initialize();
            return _currentLogFile;
        }

        /// <summary>
        /// Logs a DML operation to the audit log
        /// </summary>
        /// <param name="operation">The operation type (INSERT, UPDATE, DELETE, TRUNCATE)</param>
        /// <param name="query">The SQL query executed</param>
        /// <param name="rowsAffected">Number of rows affected</param>
        /// <param name="hadConfirmation">Whether user confirmation was provided</param>
        /// <param name="parameters">The parameters used in the query (optional)</param>
        /// <param name="executionTimeMs">Execution time in milliseconds</param>
        /// <param name="correlationId">Correlation ID for tracing</param>
        public static void LogDmlOperation(
            string operation,
            string query,
            int rowsAffected,
            bool hadConfirmation,
            Dictionary<string, object>? parameters = null,
            long executionTimeMs = 0,
            string? correlationId = null)
        {
            Initialize();

            if (_currentLogFile == null)
            {
                return;
            }

            lock (_lockObject)
            {
                CheckRotation();

                var entry = new AuditEntry
                {
                    Operation = operation,
                    Database = SqlConnectionManager.CurrentDatabase,
                    Query = TruncateQueryForAudit(query),
                    RowsAffected = rowsAffected,
                    HadConfirmation = hadConfirmation,
                    Parameters = parameters?.ToDictionary(
                        kvp => kvp.Key,
                        kvp => (object)(kvp.Value?.ToString() ?? "NULL")),
                    ExecutionTimeMs = executionTimeMs,
                    CorrelationId = correlationId,
                    ServerName = SqlConnectionManager.ServerName,
                    Environment = SqlConnectionManager.Environment
                };

                try
                {
                    var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions
                    {
                        WriteIndented = false
                    });

                    var line = json + Environment.NewLine;
                    File.AppendAllText(_currentLogFile, line);
                    _currentFileSize += line.Length;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ERROR] Failed to write audit log: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Truncates a query for audit logging to prevent excessive log size
        /// </summary>
        /// <param name="query">The original query</param>
        /// <param name="maxLength">Maximum length to log</param>
        /// <returns>Truncated query if necessary</returns>
        private static string TruncateQueryForAudit(string query, int maxLength = 4000)
        {
            if (string.IsNullOrEmpty(query)) return query;
            if (query.Length <= maxLength) return query;
            return query.Substring(0, maxLength) + $"... [truncated, original length: {query.Length}]";
        }

        /// <summary>
        /// Gets recent audit log entries (for diagnostics/monitoring)
        /// </summary>
        /// <param name="maxEntries">Maximum number of entries to return</param>
        /// <returns>List of recent audit entries</returns>
        public static List<AuditEntry> GetRecentEntries(int maxEntries = 100)
        {
            Initialize();

            if (_auditLogDir == null || !Directory.Exists(_auditLogDir))
            {
                return new List<AuditEntry>();
            }

            try
            {
                lock (_lockObject)
                {
                    // Get all audit log files for today, sorted by modification time
                    var today = DateTime.UtcNow.Date;
                    var todayFiles = Directory.GetFiles(_auditLogDir, $"dml-audit-{today:yyyy-MM-dd}*.log")
                        .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
                        .ToList();

                    var entries = new List<AuditEntry>();

                    foreach (var file in todayFiles)
                    {
                        if (entries.Count >= maxEntries) break;

                        try
                        {
                            var lines = File.ReadAllLines(file);
                            var fileEntries = lines
                                .Reverse()
                                .Take(maxEntries - entries.Count)
                                .Select(line =>
                                {
                                    try
                                    {
                                        return JsonSerializer.Deserialize<AuditEntry>(line);
                                    }
                                    catch
                                    {
                                        return null;
                                    }
                                })
                                .Where(e => e != null)
                                .Cast<AuditEntry>();

                            entries.AddRange(fileEntries);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [WARN] Failed to read audit log file {file}: {ex.Message}");
                        }
                    }

                    return entries.Take(maxEntries).ToList();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ERROR] Failed to read audit log: {ex.Message}");
                return new List<AuditEntry>();
            }
        }

        /// <summary>
        /// Gets audit log statistics for the current day
        /// </summary>
        /// <returns>Statistics object with operation counts</returns>
        public static object GetAuditStatistics()
        {
            Initialize();

            var entries = GetRecentEntries(10000);
            var today = DateTime.UtcNow.Date;

            // Get storage info
            long totalSizeBytes = 0;
            int totalFiles = 0;
            if (_auditLogDir != null && Directory.Exists(_auditLogDir))
            {
                var files = Directory.GetFiles(_auditLogDir, "dml-audit-*.log");
                totalFiles = files.Length;
                totalSizeBytes = files.Sum(f =>
                {
                    try { return new FileInfo(f).Length; } catch { return 0; }
                });
            }

            return new
            {
                date = today.ToString("yyyy-MM-dd"),
                total_entries_today = entries.Count,
                operations = new
                {
                    insert = entries.Count(e => e.Operation == "INSERT"),
                    update = entries.Count(e => e.Operation == "UPDATE"),
                    delete = entries.Count(e => e.Operation == "DELETE"),
                    truncate = entries.Count(e => e.Operation == "TRUNCATE")
                },
                with_confirmation = entries.Count(e => e.HadConfirmation),
                without_confirmation = entries.Count(e => !e.HadConfirmation),
                total_rows_affected = entries.Sum(e => e.RowsAffected),
                audit_log_path = _currentLogFile,
                storage = new
                {
                    total_files = totalFiles,
                    total_size_mb = Math.Round(totalSizeBytes / (1024.0 * 1024.0), 2),
                    retention_days = _retentionDays,
                    max_file_size_mb = _maxFileSizeBytes / (1024 * 1024)
                },
                configuration = GetRotationConfig()
            };
        }

        /// <summary>
        /// Parses an integer from environment variable
        /// </summary>
        private static int ParseIntEnv(string name, int defaultValue)
        {
            var val = Environment.GetEnvironmentVariable(name);
            return int.TryParse(val, out var parsed) && parsed > 0 ? parsed : defaultValue;
        }

        /// <summary>
        /// Parses a long from environment variable
        /// </summary>
        private static long ParseLongEnv(string name, long defaultValue)
        {
            var val = Environment.GetEnvironmentVariable(name);
            return long.TryParse(val, out var parsed) && parsed > 0 ? parsed : defaultValue;
        }
    }
}
