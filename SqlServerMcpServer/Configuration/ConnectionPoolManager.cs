using Microsoft.Data.SqlClient;
using Polly;
using Polly.CircuitBreaker;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace SqlServerMcpServer.Configuration
{
    /// <summary>
    /// Manages SQL Server connection pooling and retry logic with per-connection resilience pipelines.
    /// </summary>
    public static class ConnectionPoolManager
    {
        private static readonly int _maxRetryAttempts  = ParseIntEnv("SQLSERVER_CONNECTION_RETRY_MAX_ATTEMPTS", 3);
        private static readonly int _initialDelayMs    = ParseIntEnv("SQLSERVER_CONNECTION_RETRY_DELAY_MS", 100);
        private static readonly int _maxDelayMs        = ParseIntEnv("SQLSERVER_CONNECTION_RETRY_MAX_DELAY_MS", 5000);
        private static readonly double _backoffMultiplier = ParseDoubleEnv("SQLSERVER_CONNECTION_RETRY_BACKOFF_MULTIPLIER", 2.0);

        // Per-connection-name pipeline cache
        private static readonly ConcurrentDictionary<string, IAsyncPolicy<SqlConnection>> _pipelines =
            new(StringComparer.OrdinalIgnoreCase);

        private static int _totalConnectionAttempts = 0;
        private static int _successfulConnections   = 0;
        private static int _failedConnections       = 0;
        private static int _retriedConnections      = 0;

        private static readonly object _lockObject = new();

        static ConnectionPoolManager()
        {
            Log.Information(
                "[ConnectionPoolManager] Initialized with MaxRetries={MaxRetries}, InitialDelay={InitialDelay}ms, Multiplier={Multiplier}",
                _maxRetryAttempts, _initialDelayMs, _backoffMultiplier);
        }

        // ── public API ──────────────────────────────────────────────────────

        /// <summary>
        /// Creates an open <see cref="SqlConnection"/> with retry/circuit-breaker resilience.
        /// Uses the active connection when <paramref name="connectionName"/> is null.
        /// </summary>
        public static async Task<SqlConnection> CreateConnectionWithRetryAsync(string? connectionName = null)
        {
            lock (_lockObject) { _totalConnectionAttempts++; }

            var pipeline = GetOrCreatePipeline(connectionName ?? SqlConnectionManager.GetActiveConnectionName());

            try
            {
                var connection = await pipeline.ExecuteAsync(async () =>
                {
                    var conn = SqlConnectionManager.CreateConnection(connectionName);
                    await conn.OpenAsync();
                    return conn;
                });

                lock (_lockObject) { _successfulConnections++; }
                return connection;
            }
            catch (Exception ex)
            {
                lock (_lockObject) { _failedConnections++; }
                Log.Error(ex, "[ConnectionPoolManager] Failed to create connection '{Name}' after {Retries} retries",
                    connectionName ?? "active", _maxRetryAttempts);
                throw;
            }
        }

        /// <summary>Synchronous wrapper for <see cref="CreateConnectionWithRetryAsync"/>.</summary>
        public static SqlConnection CreateConnectionWithRetry(string? connectionName = null) =>
            CreateConnectionWithRetryAsync(connectionName).GetAwaiter().GetResult();

        /// <summary>Gets current aggregate pool statistics.</summary>
        public static PoolStatistics GetPoolStatistics()
        {
            lock (_lockObject)
            {
                return new PoolStatistics
                {
                    TotalAttempts         = _totalConnectionAttempts,
                    SuccessfulConnections = _successfulConnections,
                    FailedConnections     = _failedConnections,
                    RetriedConnections    = _retriedConnections,
                    SuccessRate           = _totalConnectionAttempts > 0
                        ? _successfulConnections / (double)_totalConnectionAttempts * 100
                        : 0,
                    RetryRate             = _totalConnectionAttempts > 0
                        ? _retriedConnections / (double)_totalConnectionAttempts * 100
                        : 0
                };
            }
        }

        /// <summary>
        /// Returns the retry policy for the active connection.
        /// Kept for backward compatibility with existing tests.
        /// </summary>
        public static IAsyncPolicy<SqlConnection> GetRetryPolicy() =>
            GetOrCreatePipeline(SqlConnectionManager.GetActiveConnectionName());

        /// <summary>Resets aggregate statistics.</summary>
        public static void ResetStatistics()
        {
            lock (_lockObject)
            {
                _totalConnectionAttempts = 0;
                _successfulConnections   = 0;
                _failedConnections       = 0;
                _retriedConnections      = 0;
            }
            Log.Information("[ConnectionPoolManager] Statistics reset");
        }

        // ── pipeline management ──────────────────────────────────────────────

        private static IAsyncPolicy<SqlConnection> GetOrCreatePipeline(string connectionName) =>
            _pipelines.GetOrAdd(connectionName, BuildPipeline);

        private static IAsyncPolicy<SqlConnection> BuildPipeline(string connectionName)
        {
            var retryPolicy = Policy
                .Handle<SqlException>(IsTransientError)
                .Or<TimeoutException>()
                .OrResult<SqlConnection>(conn => conn == null)
                .WaitAndRetryAsync<SqlConnection>(
                    retryCount: _maxRetryAttempts,
                    sleepDurationProvider: attempt =>
                        TimeSpan.FromMilliseconds(Math.Min(
                            _initialDelayMs * Math.Pow(_backoffMultiplier, attempt - 1),
                            _maxDelayMs)),
                    onRetry: (outcome, timespan, retryCount, _) =>
                    {
                        lock (_lockObject) { _retriedConnections++; }
                        Log.Warning(
                            "[ConnectionPoolManager] [{Conn}] Retry {RetryCount}/{MaxRetries} after {Delay}ms. Reason: {Reason}",
                            connectionName, retryCount, _maxRetryAttempts, timespan.TotalMilliseconds,
                            outcome.Exception?.Message ?? "null connection");
                    });

            var circuitBreaker = Policy
                .Handle<SqlException>(IsTransientError)
                .Or<TimeoutException>()
                .OrResult<SqlConnection>(conn => conn == null)
                .CircuitBreakerAsync<SqlConnection>(
                    handledEventsAllowedBeforeBreaking: 5,
                    durationOfBreak: TimeSpan.FromSeconds(30),
                    onBreak: (outcome, duration) =>
                        Log.Error(
                            "[ConnectionPoolManager] [{Conn}] Circuit breaker OPEN for {Duration}s. Failure: {Reason}",
                            connectionName, duration.TotalSeconds,
                            outcome.Exception?.Message ?? "multiple failures"),
                    onReset: () =>
                        Log.Information("[ConnectionPoolManager] [{Conn}] Circuit breaker RESET", connectionName));

            return Policy.WrapAsync(circuitBreaker, retryPolicy);
        }

        private static bool IsTransientError(SqlException ex)
        {
            int[] transient = { -2, -1, 2, 53, 233, 40197, 40501, 40613, 49918, 49919, 49920 };
            foreach (SqlError error in ex.Errors)
            {
                foreach (var n in transient)
                    if (error.Number == n) return true;
            }
            return false;
        }

        private static int ParseIntEnv(string name, int defaultValue)
        {
            var val = System.Environment.GetEnvironmentVariable(name);
            return int.TryParse(val, out var parsed) && parsed > 0 ? parsed : defaultValue;
        }

        private static double ParseDoubleEnv(string name, double defaultValue)
        {
            var val = System.Environment.GetEnvironmentVariable(name);
            return double.TryParse(val, out var parsed) && parsed > 0 ? parsed : defaultValue;
        }
    }

    /// <summary>Aggregate connection pool statistics.</summary>
    public class PoolStatistics
    {
        public int    TotalAttempts         { get; set; }
        public int    SuccessfulConnections  { get; set; }
        public int    FailedConnections      { get; set; }
        public int    RetriedConnections     { get; set; }
        public double SuccessRate            { get; set; }
        public double RetryRate              { get; set; }
    }
}
