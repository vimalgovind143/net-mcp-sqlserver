using System.Collections.Concurrent;

namespace SqlServerMcpServer.Security
{
    /// <summary>
    /// Provides rate limiting for query execution to prevent abuse
    /// </summary>
    public static class RateLimiter
    {
        // Configuration - can be overridden via environment variables
        private static readonly int _maxRequestsPerMinute = ParseIntEnv("MCP_RATE_LIMIT_REQUESTS_PER_MINUTE", 60);
        private static readonly int _maxDmlRequestsPerMinute = ParseIntEnv("MCP_RATE_LIMIT_DML_PER_MINUTE", 20);
        private static readonly int _burstAllowance = ParseIntEnv("MCP_RATE_LIMIT_BURST", 10);
        private static readonly int _windowSeconds = 60;
        private static readonly bool _rateLimitingEnabled = ParseBoolEnv("MCP_RATE_LIMITING_ENABLED", true);

        // In-memory rate limit store
        // Key: client identifier (correlation ID or session), Value: request history
        private static readonly ConcurrentDictionary<string, ClientRateLimitInfo> _clients = new();
        private static readonly Timer _cleanupTimer;

        /// <summary>
        /// Information about a client's rate limit status
        /// </summary>
        private class ClientRateLimitInfo
        {
            public Queue<DateTime> RequestTimes { get; } = new Queue<DateTime>();
            public Queue<DateTime> DmlRequestTimes { get; } = new Queue<DateTime>();
            public DateTime LastAccess { get; set; } = DateTime.UtcNow;
            public int ViolationCount { get; set; }
        }

        /// <summary>
        /// Result of a rate limit check
        /// </summary>
        public class RateLimitResult
        {
            public bool IsAllowed { get; set; }
            public int RemainingRequests { get; set; }
            public int RemainingDmlRequests { get; set; }
            public TimeSpan? RetryAfter { get; set; }
            public string? BlockReason { get; set; }
            public int CurrentRequests { get; set; }
            public int CurrentDmlRequests { get; set; }
        }

        /// <summary>
        /// Rate limiter configuration
        /// </summary>
        public class RateLimitConfig
        {
            public bool Enabled { get; set; }
            public int MaxRequestsPerMinute { get; set; }
            public int MaxDmlRequestsPerMinute { get; set; }
            public int BurstAllowance { get; set; }
            public int WindowSeconds { get; set; }
        }

        /// <summary>
        /// Static constructor to initialize cleanup timer
        /// </summary>
        static RateLimiter()
        {
            // Clean up old entries every 5 minutes
            _cleanupTimer = new Timer(CleanupOldEntries, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// Gets the current rate limiter configuration
        /// </summary>
        public static RateLimitConfig GetConfig()
        {
            return new RateLimitConfig
            {
                Enabled = _rateLimitingEnabled,
                MaxRequestsPerMinute = _maxRequestsPerMinute,
                MaxDmlRequestsPerMinute = _maxDmlRequestsPerMinute,
                BurstAllowance = _burstAllowance,
                WindowSeconds = _windowSeconds
            };
        }

        /// <summary>
        /// Checks if a request is allowed under rate limits
        /// </summary>
        /// <param name="clientId">Client identifier (correlation ID or session)</param>
        /// <param name="isDml">Whether this is a DML operation</param>
        /// <returns>Rate limit result</returns>
        public static RateLimitResult CheckRateLimit(string clientId, bool isDml = false)
        {
            if (!_rateLimitingEnabled)
            {
                return new RateLimitResult
                {
                    IsAllowed = true,
                    RemainingRequests = int.MaxValue,
                    RemainingDmlRequests = int.MaxValue
                };
            }

            if (string.IsNullOrWhiteSpace(clientId))
            {
                clientId = "anonymous";
            }

            var now = DateTime.UtcNow;
            var windowStart = now.AddSeconds(-_windowSeconds);

            var clientInfo = _clients.GetOrAdd(clientId, _ => new ClientRateLimitInfo());

            lock (clientInfo)
            {
                // Update last access
                clientInfo.LastAccess = now;

                // Remove old entries outside the window
                while (clientInfo.RequestTimes.Count > 0 && clientInfo.RequestTimes.Peek() < windowStart)
                {
                    clientInfo.RequestTimes.Dequeue();
                }
                while (clientInfo.DmlRequestTimes.Count > 0 && clientInfo.DmlRequestTimes.Peek() < windowStart)
                {
                    clientInfo.DmlRequestTimes.Dequeue();
                }

                // Check rate limits
                var currentRequests = clientInfo.RequestTimes.Count;
                var currentDmlRequests = clientInfo.DmlRequestTimes.Count;

                // Check if DML limit is exceeded
                if (isDml && currentDmlRequests >= _maxDmlRequestsPerMinute)
                {
                    var oldestDml = clientInfo.DmlRequestTimes.Peek();
                    var retryAfter = oldestDml.AddSeconds(_windowSeconds) - now;

                    clientInfo.ViolationCount++;

                    return new RateLimitResult
                    {
                        IsAllowed = false,
                        RemainingRequests = Math.Max(0, _maxRequestsPerMinute - currentRequests),
                        RemainingDmlRequests = 0,
                        RetryAfter = retryAfter,
                        BlockReason = $"DML rate limit exceeded. Maximum {_maxDmlRequestsPerMinute} DML operations per minute allowed.",
                        CurrentRequests = currentRequests,
                        CurrentDmlRequests = currentDmlRequests
                    };
                }

                // Check if general rate limit is exceeded (with burst allowance)
                var effectiveLimit = _maxRequestsPerMinute + _burstAllowance;
                if (currentRequests >= effectiveLimit)
                {
                    var oldestRequest = clientInfo.RequestTimes.Peek();
                    var retryAfter = oldestRequest.AddSeconds(_windowSeconds) - now;

                    clientInfo.ViolationCount++;

                    return new RateLimitResult
                    {
                        IsAllowed = false,
                        RemainingRequests = 0,
                        RemainingDmlRequests = Math.Max(0, _maxDmlRequestsPerMinute - currentDmlRequests),
                        RetryAfter = retryAfter,
                        BlockReason = $"Rate limit exceeded. Maximum {_maxRequestsPerMinute} requests per minute allowed (with {_burstAllowance} burst).",
                        CurrentRequests = currentRequests,
                        CurrentDmlRequests = currentDmlRequests
                    };
                }

                // Record this request
                clientInfo.RequestTimes.Enqueue(now);
                if (isDml)
                {
                    clientInfo.DmlRequestTimes.Enqueue(now);
                }

                return new RateLimitResult
                {
                    IsAllowed = true,
                    RemainingRequests = effectiveLimit - currentRequests - 1,
                    RemainingDmlRequests = isDml 
                        ? _maxDmlRequestsPerMinute - currentDmlRequests - 1
                        : _maxDmlRequestsPerMinute - currentDmlRequests,
                    CurrentRequests = currentRequests + 1,
                    CurrentDmlRequests = isDml ? currentDmlRequests + 1 : currentDmlRequests
                };
            }
        }

        /// <summary>
        /// Records a request for rate limiting purposes
        /// </summary>
        /// <param name="clientId">Client identifier</param>
        /// <param name="isDml">Whether this is a DML operation</param>
        /// <returns>Rate limit result</returns>
        public static RateLimitResult RecordRequest(string clientId, bool isDml = false)
        {
            return CheckRateLimit(clientId, isDml);
        }

        /// <summary>
        /// Gets rate limit statistics for a client
        /// </summary>
        /// <param name="clientId">Client identifier</param>
        /// <returns>Rate limit statistics</returns>
        public static object GetClientStatistics(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                clientId = "anonymous";
            }

            if (!_clients.TryGetValue(clientId, out var clientInfo))
            {
                return new
                {
                    client_id = clientId,
                    current_requests = 0,
                    current_dml_requests = 0,
                    remaining_requests = _maxRequestsPerMinute + _burstAllowance,
                    remaining_dml_requests = _maxDmlRequestsPerMinute,
                    violation_count = 0,
                    is_limited = false
                };
            }

            lock (clientInfo)
            {
                var now = DateTime.UtcNow;
                var windowStart = now.AddSeconds(-_windowSeconds);

                // Clean up old entries
                while (clientInfo.RequestTimes.Count > 0 && clientInfo.RequestTimes.Peek() < windowStart)
                {
                    clientInfo.RequestTimes.Dequeue();
                }
                while (clientInfo.DmlRequestTimes.Count > 0 && clientInfo.DmlRequestTimes.Peek() < windowStart)
                {
                    clientInfo.DmlRequestTimes.Dequeue();
                }

                var currentRequests = clientInfo.RequestTimes.Count;
                var currentDmlRequests = clientInfo.DmlRequestTimes.Count;
                var isLimited = currentRequests >= _maxRequestsPerMinute + _burstAllowance ||
                               currentDmlRequests >= _maxDmlRequestsPerMinute;

                return new
                {
                    client_id = clientId,
                    current_requests = currentRequests,
                    current_dml_requests = currentDmlRequests,
                    remaining_requests = Math.Max(0, _maxRequestsPerMinute + _burstAllowance - currentRequests),
                    remaining_dml_requests = Math.Max(0, _maxDmlRequestsPerMinute - currentDmlRequests),
                    violation_count = clientInfo.ViolationCount,
                    is_limited = isLimited,
                    last_access = clientInfo.LastAccess
                };
            }
        }

        /// <summary>
        /// Gets global rate limit statistics
        /// </summary>
        /// <returns>Global statistics</returns>
        public static object GetGlobalStatistics()
        {
            var now = DateTime.UtcNow;
            var windowStart = now.AddSeconds(-_windowSeconds);
            var totalRequests = 0;
            var totalDmlRequests = 0;
            var totalViolations = 0;
            var activeClients = 0;

            foreach (var client in _clients.Values)
            {
                lock (client)
                {
                    // Only count recently active clients
                    if (client.LastAccess > windowStart)
                    {
                        activeClients++;
                        totalRequests += client.RequestTimes.Count;
                        totalDmlRequests += client.DmlRequestTimes.Count;
                        totalViolations += client.ViolationCount;
                    }
                }
            }

            return new
            {
                configuration = GetConfig(),
                active_clients = activeClients,
                total_requests_in_window = totalRequests,
                total_dml_requests_in_window = totalDmlRequests,
                total_violations = totalViolations,
                window_seconds = _windowSeconds
            };
        }

        /// <summary>
        /// Resets rate limits for a specific client
        /// </summary>
        /// <param name="clientId">Client identifier</param>
        public static void ResetClientLimits(string clientId)
        {
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                _clients.TryRemove(clientId, out _);
            }
        }

        /// <summary>
        /// Cleans up old client entries
        /// </summary>
        private static void CleanupOldEntries(object? state)
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddMinutes(-10); // Remove clients inactive for 10 minutes
                var keysToRemove = new List<string>();

                foreach (var kvp in _clients)
                {
                    if (kvp.Value.LastAccess < cutoff)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    _clients.TryRemove(key, out _);
                }

                if (keysToRemove.Count > 0)
                {
                    Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] Cleaned up {keysToRemove.Count} inactive rate limit entries");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ERROR] Rate limit cleanup failed: {ex.Message}");
            }
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
        /// Parses a boolean from environment variable
        /// </summary>
        private static bool ParseBoolEnv(string name, bool defaultValue)
        {
            var val = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(val)) return defaultValue;
            return bool.TryParse(val, out var parsed) ? parsed : defaultValue;
        }
    }
}
