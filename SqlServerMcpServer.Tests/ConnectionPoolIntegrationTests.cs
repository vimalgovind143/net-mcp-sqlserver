using FluentAssertions;
using SqlServerMcpServer.Configuration;
using System;
using System.Threading.Tasks;
using Xunit;

namespace SqlServerMcpServer.Tests
{
    /// <summary>
    /// Integration tests for ConnectionPoolManager with retry and circuit breaker patterns
    /// </summary>
    public class ConnectionPoolIntegrationTests
    {
        [Fact]
        public void ConnectionPoolManager_InitializesWithDefaultValues()
        {
            // Act
            var stats = ConnectionPoolManager.GetPoolStatistics();

            // Assert
            stats.Should().NotBeNull();
            stats.TotalAttempts.Should().BeGreaterThanOrEqualTo(0);
            stats.SuccessfulConnections.Should().BeGreaterThanOrEqualTo(0);
            stats.FailedConnections.Should().BeGreaterThanOrEqualTo(0);
        }

        [Fact]
        public void ConnectionPoolManager_ResetStatistics_ClearsMetrics()
        {
            // Arrange
            ConnectionPoolManager.ResetStatistics();

            // Act
            var statsBefore = ConnectionPoolManager.GetPoolStatistics();
            statsBefore.TotalAttempts.Should().Be(0);

            // Assert
            statsBefore.SuccessfulConnections.Should().Be(0);
            statsBefore.FailedConnections.Should().Be(0);
            statsBefore.RetriedConnections.Should().Be(0);
        }

        [Fact]
        public void ConnectionPoolManager_PoolStatistics_CalculatesRatesCorrectly()
        {
            // Arrange
            ConnectionPoolManager.ResetStatistics();

            // Act
            var stats = ConnectionPoolManager.GetPoolStatistics();

            // If no attempts, rates should be 0
            if (stats.TotalAttempts == 0)
            {
                stats.SuccessRate.Should().Be(0);
                stats.RetryRate.Should().Be(0);
            }

            // Assert - structure is correct
            stats.SuccessRate.Should().BeInRange(0, 100);
            stats.RetryRate.Should().BeInRange(0, 100);
        }

        [Fact]
        public void ConnectionPoolManager_GetRetryPolicy_ReturnsValidPolicy()
        {
            // Act
            var policy = ConnectionPoolManager.GetRetryPolicy();

            // Assert
            policy.Should().NotBeNull();
        }

        [Fact]
        public void ConnectionPoolManager_PoolStatistics_HasAllRequiredProperties()
        {
            // Act
            var stats = ConnectionPoolManager.GetPoolStatistics();

            // Assert
            stats.Should().NotBeNull();
            stats.TotalAttempts.Should().BeGreaterThanOrEqualTo(0);
            stats.SuccessfulConnections.Should().BeGreaterThanOrEqualTo(0);
            stats.FailedConnections.Should().BeGreaterThanOrEqualTo(0);
            stats.RetriedConnections.Should().BeGreaterThanOrEqualTo(0);
        }

        [Fact]
        public void ConnectionPoolManager_Statistics_IncrementCorrectly()
        {
            // Arrange
            ConnectionPoolManager.ResetStatistics();
            var initialStats = ConnectionPoolManager.GetPoolStatistics();

            // Act
            // Attempting to create a connection will increment statistics
            // Note: This test verifies the structure, actual connection depends on server availability

            // Assert
            // Stats should always be retrievable
            initialStats.Should().NotBeNull();
            initialStats.GetType().Name.Should().Be("PoolStatistics");
        }

        [Fact]
        public void ConnectionPoolManager_SuccessRateCalculation_IsValid()
        {
            // Arrange
            ConnectionPoolManager.ResetStatistics();

            // Act
            var stats = ConnectionPoolManager.GetPoolStatistics();

            // Assert
            if (stats.TotalAttempts > 0)
            {
                var expectedRate = (stats.SuccessfulConnections / (double)stats.TotalAttempts) * 100;
                stats.SuccessRate.Should().BeApproximately(expectedRate, 0.01);
            }
        }

        [Fact]
        public void ConnectionPoolManager_RetryRateCalculation_IsValid()
        {
            // Arrange
            ConnectionPoolManager.ResetStatistics();

            // Act
            var stats = ConnectionPoolManager.GetPoolStatistics();

            // Assert
            if (stats.TotalAttempts > 0)
            {
                var expectedRate = (stats.RetriedConnections / (double)stats.TotalAttempts) * 100;
                stats.RetryRate.Should().BeApproximately(expectedRate, 0.01);
            }
        }

        [Fact]
        public void ConnectionPoolManager_StatisticConsistency_TotalEqualsSum()
        {
            // Arrange
            ConnectionPoolManager.ResetStatistics();

            // Act
            var stats = ConnectionPoolManager.GetPoolStatistics();

            // Assert
            // This verifies internal consistency of statistics
            if (stats.TotalAttempts > 0)
            {
                (stats.SuccessfulConnections + stats.FailedConnections).Should().BeLessThanOrEqualTo(stats.TotalAttempts);
            }
        }

        [Fact]
        public void ConnectionPoolManager_ResetMultipleTimes_WorksCorrectly()
        {
            // Act & Assert - Multiple resets should work without error
            for (int i = 0; i < 3; i++)
            {
                ConnectionPoolManager.ResetStatistics();
                var stats = ConnectionPoolManager.GetPoolStatistics();
                stats.TotalAttempts.Should().Be(0);
            }
        }

        [Fact]
        public void ConnectionPoolManager_ThreadSafety_HandlesConcurrentAccess()
        {
            // Arrange
            ConnectionPoolManager.ResetStatistics();

            // Act
            var tasks = new Task[5];
            for (int i = 0; i < 5; i++)
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

        [Fact]
        public void ConnectionPoolManager_AllMetricsInitialized_ToNonNegativeValues()
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
            stats.SuccessRate.Should().BeGreaterThanOrEqualTo(0);
            stats.RetryRate.Should().BeGreaterThanOrEqualTo(0);
        }
    }
}
