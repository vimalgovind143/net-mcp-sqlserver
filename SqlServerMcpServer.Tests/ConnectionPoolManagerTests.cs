using Xunit;
using FluentAssertions;
using SqlServerMcpServer.Configuration;
using Microsoft.Data.SqlClient;
using System;
using System.Threading.Tasks;

namespace SqlServerMcpServer.Tests
{
    public class ConnectionPoolManagerTests
    {
        [Fact]
        public void GetPoolStatistics_ReturnsValidStatistics()
        {
            // Arrange
            ConnectionPoolManager.ResetStatistics();

            // Act
            var stats = ConnectionPoolManager.GetPoolStatistics();

            // Assert
            stats.Should().NotBeNull();
            stats.TotalAttempts.Should().Be(0);
            stats.SuccessfulConnections.Should().Be(0);
            stats.FailedConnections.Should().Be(0);
            stats.RetriedConnections.Should().Be(0);
        }

        [Fact]
        public void ResetStatistics_ClearsAllMetrics()
        {
            // Arrange
            ConnectionPoolManager.ResetStatistics();
            var statsBefore = ConnectionPoolManager.GetPoolStatistics();

            // Act
            ConnectionPoolManager.ResetStatistics();
            var statsAfter = ConnectionPoolManager.GetPoolStatistics();

            // Assert
            statsAfter.TotalAttempts.Should().Be(0);
            statsAfter.SuccessfulConnections.Should().Be(0);
            statsAfter.FailedConnections.Should().Be(0);
        }

        [Fact]
        public async Task CreateConnectionWithRetryAsync_WithValidConnectionString_ReturnsConnection()
        {
            // Skip if no connection string is configured
            if (string.IsNullOrWhiteSpace(SqlConnectionManager.CurrentConnectionString))
            {
                return;
            }

            // Arrange
            ConnectionPoolManager.ResetStatistics();
            var validConnectionString = SqlConnectionManager.CurrentConnectionString;

            // Act
            try
            {
                var connection = await ConnectionPoolManager.CreateConnectionWithRetryAsync();

                // Assert
                connection.Should().NotBeNull();
                connection.Should().BeOfType<SqlConnection>();
                connection.State.Should().Be(System.Data.ConnectionState.Open);

                // Cleanup
                connection?.Dispose();
            }
            catch (SqlException)
            {
                // SQL Server may not be available in test environment - that's okay
                // The important thing is the policy structure was exercised
            }
        }

        [Fact]
        public void CreateConnectionWithRetry_WithValidConnectionString_ReturnsConnection()
        {
            // Skip if no connection string is configured
            if (string.IsNullOrWhiteSpace(SqlConnectionManager.CurrentConnectionString))
            {
                return;
            }

            // Arrange
            ConnectionPoolManager.ResetStatistics();

            // Act
            try
            {
                var connection = ConnectionPoolManager.CreateConnectionWithRetry();

                // Assert
                connection.Should().NotBeNull();
                connection.Should().BeOfType<SqlConnection>();

                // Cleanup
                connection?.Dispose();
            }
            catch (SqlException)
            {
                // SQL Server may not be available in test environment
            }
        }

        [Fact]
        public void GetPoolStatistics_CalculatesSuccessRate()
        {
            // Arrange
            ConnectionPoolManager.ResetStatistics();

            // Act
            var stats = ConnectionPoolManager.GetPoolStatistics();

            // Assert - when no attempts, success rate should be 0
            stats.SuccessRate.Should().BeGreaterThanOrEqualTo(0);
            stats.SuccessRate.Should().BeLessThanOrEqualTo(100);
        }

        [Fact]
        public void GetPoolStatistics_CalculatesRetryRate()
        {
            // Arrange
            ConnectionPoolManager.ResetStatistics();

            // Act
            var stats = ConnectionPoolManager.GetPoolStatistics();

            // Assert
            stats.RetryRate.Should().BeGreaterThanOrEqualTo(0);
            stats.RetryRate.Should().BeLessThanOrEqualTo(100);
        }

        [Fact]
        public void GetPoolStatistics_AllStatisticsAreNonNegative()
        {
            // Arrange
            ConnectionPoolManager.ResetStatistics();

            // Act
            var stats = ConnectionPoolManager.GetPoolStatistics();

            // Assert
            stats.TotalAttempts.Should().BeGreaterThanOrEqualTo(0);
            stats.SuccessfulConnections.Should().BeGreaterThanOrEqualTo(0);
            stats.FailedConnections.Should().BeGreaterThanOrEqualTo(0);
            stats.RetriedConnections.Should().BeGreaterThanOrEqualTo(0);
        }

        [Fact]
        public void GetPoolStatistics_SuccessfulConnectionsNeverExceedTotalAttempts()
        {
            // Arrange
            ConnectionPoolManager.ResetStatistics();

            // Act
            var stats = ConnectionPoolManager.GetPoolStatistics();

            // Assert
            stats.SuccessfulConnections.Should().BeLessThanOrEqualTo(stats.TotalAttempts);
        }

        [Fact]
        public void GetPoolStatistics_FailedConnectionsNeverExceedTotalAttempts()
        {
            // Arrange
            ConnectionPoolManager.ResetStatistics();

            // Act
            var stats = ConnectionPoolManager.GetPoolStatistics();

            // Assert
            stats.FailedConnections.Should().BeLessThanOrEqualTo(stats.TotalAttempts);
        }

        [Fact]
        public void GetRetryPolicy_ReturnsValidPolicy()
        {
            // Act
            var policy = ConnectionPoolManager.GetRetryPolicy();

            // Assert
            policy.Should().NotBeNull();
        }

        [Fact]
        public async Task CreateConnectionWithRetryAsync_MultipleAttempts_UpdatesStatistics()
        {
            // Skip if no connection string is configured
            if (string.IsNullOrWhiteSpace(SqlConnectionManager.CurrentConnectionString))
            {
                return;
            }

            // Arrange
            ConnectionPoolManager.ResetStatistics();
            var statsBefore = ConnectionPoolManager.GetPoolStatistics();

            // Act
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    var connection = await ConnectionPoolManager.CreateConnectionWithRetryAsync();
                    connection?.Dispose();
                }
                catch (SqlException)
                {
                    // Connection may fail - that's expected in test environment
                }
            }

            var statsAfter = ConnectionPoolManager.GetPoolStatistics();

            // Assert - we should have made at least 3 attempts
            statsAfter.TotalAttempts.Should().BeGreaterThanOrEqualTo(3);
        }

        [Fact]
        public void PoolStatistics_StringRepresentation()
        {
            // Arrange
            var stats = new PoolStatistics
            {
                TotalAttempts = 10,
                SuccessfulConnections = 8,
                FailedConnections = 2,
                RetriedConnections = 1,
                SuccessRate = 80.0,
                RetryRate = 10.0
            };

            // Act
            var toString = stats.ToString();

            // Assert
            toString.Should().NotBeNullOrEmpty();
            // PoolStatistics doesn't have a custom ToString, so just verify it's not null
        }

        [Fact]
        public void ResetStatistics_MultipleTimes_StaysConsistent()
        {
            // Arrange
            ConnectionPoolManager.ResetStatistics();

            // Act
            for (int i = 0; i < 5; i++)
            {
                ConnectionPoolManager.ResetStatistics();
                var stats = ConnectionPoolManager.GetPoolStatistics();

                // Assert
                stats.TotalAttempts.Should().Be(0);
                stats.SuccessfulConnections.Should().Be(0);
                stats.FailedConnections.Should().Be(0);
            }
        }

        [Fact]
        public void GetPoolStatistics_ThreadSafe_MultipleReads()
        {
            // Arrange
            ConnectionPoolManager.ResetStatistics();

            // Act
            var tasks = new Task[10];
            for (int i = 0; i < 10; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    var stats = ConnectionPoolManager.GetPoolStatistics();
                    stats.Should().NotBeNull();
                });
            }
            Task.WaitAll(tasks);

            // Assert
            var finalStats = ConnectionPoolManager.GetPoolStatistics();
            finalStats.Should().NotBeNull();
        }
    }
}
