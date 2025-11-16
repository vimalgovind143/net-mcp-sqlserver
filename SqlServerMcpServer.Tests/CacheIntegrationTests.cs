using FluentAssertions;
using SqlServerMcpServer.Utilities;
using System;
using System.Threading.Tasks;
using Xunit;

namespace SqlServerMcpServer.Tests
{
    /// <summary>
    /// Integration tests for CacheService with real-world scenarios
    /// </summary>
    public class CacheIntegrationTests
    {
        private readonly CacheService _cacheService = new();

        [Fact]
        public void CacheService_StoresAndRetrievesValue_Successfully()
        {
            // Arrange
            var key = "test_key";
            var value = "test_value";

            // Act
            _cacheService.Set(key, value);
            var result = _cacheService.GetOrCreate(key, () => "fallback");

            // Assert
            result.Should().Be(value);
            var metrics = _cacheService.GetMetrics();
            metrics.Hits.Should().Be(1);
            metrics.Misses.Should().Be(0);
        }

        [Fact]
        public void CacheService_WithMultipleOperations_TracksMetricsCorrectly()
        {
            // Arrange
            _cacheService.ResetMetrics();

            // Act
            _cacheService.Set("key1", "value1");
            _cacheService.GetOrCreate("key1", () => "fallback"); // Hit
            _cacheService.GetOrCreate("key2", () => "value2");   // Miss
            _cacheService.GetOrCreate("key2", () => "fallback"); // Hit

            // Assert
            var metrics = _cacheService.GetMetrics();
            metrics.Hits.Should().Be(2);
            metrics.Misses.Should().Be(1);
            metrics.TotalOperations.Should().Be(3);
            metrics.HitRatio.Should().BeApproximately(66.67, 0.1);
        }

        [Fact]
        public async Task CacheService_AsyncOperations_WorkCorrectly()
        {
            // Arrange
            var key = "async_key";
            var expectedValue = "async_value";

            // Act
            await _cacheService.SetAsync(key, expectedValue);
            var result = await _cacheService.GetOrCreateAsync(key, async () =>
            {
                await Task.Delay(10);
                return "fallback";
            });

            // Assert
            result.Should().Be(expectedValue);
            var metrics = _cacheService.GetMetrics();
            metrics.Hits.Should().Be(1);
        }

        [Fact]
        public void CacheService_RemoveByPattern_DeletesMatchingKeys()
        {
            // Arrange
            _cacheService.Set("tables:db1", "data1");
            _cacheService.Set("tables:db2", "data2");
            _cacheService.Set("schema:db1", "schema1");

            // Act
            var removedCount = _cacheService.RemoveByPattern("tables:*");

            // Assert
            removedCount.Should().Be(2);
            var info = _cacheService.GetCacheInfo();
            info.CurrentEntriesCount.Should().Be(1);
        }

        [Fact]
        public void CacheService_WithDifferentTTLs_ReturnsCorrectConfiguration()
        {
            // Act
            var ttlMetadata = CacheService.GetTTLForType("metadata");
            var ttlSchema = CacheService.GetTTLForType("schema");
            var ttlProcedure = CacheService.GetTTLForType("procedure");

            // Assert
            ttlMetadata.TotalSeconds.Should().Be(300);
            ttlSchema.TotalSeconds.Should().Be(600);
            ttlProcedure.TotalSeconds.Should().Be(300);
        }

        [Fact]
        public void CacheService_GeneratesCacheKeysCorrectly()
        {
            // Act
            var tableKey = CacheService.GenerateTablesCacheKey("mydb", "dbo", "Users");
            var schemaKey = CacheService.GenerateSchemaCacheKey("mydb", "Users");
            var procKey = CacheService.GenerateProceduresCacheKey("mydb", "dbo");

            // Assert
            tableKey.Should().Be("tables:mydb:dbo:Users");
            schemaKey.Should().Be("schema:mydb:Users");
            procKey.Should().Be("procedures:mydb:dbo");
        }

        [Fact]
        public void CacheService_GetCacheInfo_ReturnsCompleteInformation()
        {
            // Arrange
            _cacheService.Set("tables:db1:table1", new { name = "test" });
            _cacheService.Set("schema:db1:table1", new { columns = 5 });
            _cacheService.Set("procedures:db1", new { count = 10 });

            // Act
            var info = _cacheService.GetCacheInfo();

            // Assert
            info.Enabled.Should().BeTrue();
            info.DefaultTTLSeconds.Should().Be(300);
            info.SchemaTTLSeconds.Should().Be(600);
            info.ProcedureTTLSeconds.Should().Be(300);
            info.CurrentEntriesCount.Should().Be(3);
            info.CachedKeyPrefixes["tables"].Should().Be(1);
            info.CachedKeyPrefixes["schema"].Should().Be(1);
            info.CachedKeyPrefixes["procedures"].Should().Be(1);
        }

        [Fact]
        public void CacheService_HitRatioCalculation_IsAccurate()
        {
            // Arrange
            _cacheService.ResetMetrics();
            _cacheService.Set("key1", "value1");

            // Act - 3 hits, 2 misses = 60% hit ratio
            _cacheService.GetOrCreate("key1", () => "fallback");
            _cacheService.GetOrCreate("key1", () => "fallback");
            _cacheService.GetOrCreate("key1", () => "fallback");
            _cacheService.GetOrCreate("key2", () => "value2");
            _cacheService.GetOrCreate("key3", () => "value3");

            // Assert
            var metrics = _cacheService.GetMetrics();
            metrics.Hits.Should().Be(3);
            metrics.Misses.Should().Be(2);
            metrics.HitRatio.Should().BeApproximately(60.0, 0.1);
        }

        [Fact]
        public void CacheService_MetricsSnapshot_CapturesPointInTime()
        {
            // Arrange
            _cacheService.ResetMetrics();
            _cacheService.Set("key1", "value1");
            _cacheService.GetOrCreate("key1", () => "fallback");

            // Act
            var snapshot1 = _cacheService.GetMetrics();
            System.Threading.Thread.Sleep(10);
            _cacheService.GetOrCreate("key1", () => "fallback");
            var snapshot2 = _cacheService.GetMetrics();

            // Assert
            snapshot1.Hits.Should().Be(1);
            snapshot2.Hits.Should().Be(2);
            snapshot2.Timestamp.Should().BeAfter(snapshot1.Timestamp);
        }

        [Fact]
        public void CacheService_ResetMetrics_ClearsAllCounts()
        {
            // Arrange
            _cacheService.Set("key1", "value1");
            _cacheService.GetOrCreate("key1", () => "fallback");

            var beforeReset = _cacheService.GetMetrics();
            beforeReset.TotalOperations.Should().BeGreaterThan(0);

            // Act
            _cacheService.ResetMetrics();

            // Assert
            var afterReset = _cacheService.GetMetrics();
            afterReset.Hits.Should().Be(0);
            afterReset.Misses.Should().Be(0);
            afterReset.TotalOperations.Should().Be(0);
        }

        [Fact]
        public void CacheService_StoringNullValue_HandlesCorrectly()
        {
            // Arrange
            var key = "null_key";
            object? nullValue = null;

            // Act
            _cacheService.Set(key, nullValue);
            var result = _cacheService.GetOrCreate(key, () => "default");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void CacheService_Remove_DeletesSpecificKey()
        {
            // Arrange
            _cacheService.Set("key1", "value1");
            _cacheService.Set("key2", "value2");

            // Act
            _cacheService.Remove("key1");

            // Assert
            var info = _cacheService.GetCacheInfo();
            info.CurrentEntriesCount.Should().Be(1);
        }
    }
}
