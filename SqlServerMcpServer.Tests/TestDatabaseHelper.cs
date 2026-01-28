using System;
using Microsoft.Data.SqlClient;
using SqlServerMcpServer.Configuration;

namespace SqlServerMcpServer.Tests
{
    internal static class TestDatabaseHelper
    {
        private static readonly Lazy<Exception?> ConnectionError = new Lazy<Exception?>(TryOpenConnection);
        private static readonly Lazy<Exception?> ViewServerStateError = new Lazy<Exception?>(TryCheckViewServerState);

        public static bool IsDatabaseAvailable => ConnectionError.Value is null;

        public static bool HasViewServerState => IsDatabaseAvailable && ViewServerStateError.Value is null;

        private static Exception? TryOpenConnection()
        {
            try
            {
                using var connection = new SqlConnection(BuildTestConnectionString());
                connection.Open();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        private static Exception? TryCheckViewServerState()
        {
            try
            {
                using var connection = new SqlConnection(BuildTestConnectionString());
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = "SELECT HAS_PERMS_BY_NAME(NULL, NULL, 'VIEW SERVER STATE')";
                var result = command.ExecuteScalar();
                if (result is int perm && perm == 1)
                {
                    return null;
                }
                if (result is long longPerm && longPerm == 1L)
                {
                    return null;
                }

                return new InvalidOperationException("VIEW SERVER STATE permission not granted.");
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        private static string BuildTestConnectionString()
        {
            var baseConnectionString = SqlConnectionManager.CurrentConnectionString ?? string.Empty;
            if (baseConnectionString.IndexOf("Connect Timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                baseConnectionString.IndexOf("Connection Timeout", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return baseConnectionString;
            }

            return baseConnectionString.TrimEnd(';') + ";Connect Timeout=2";
        }
    }
}
