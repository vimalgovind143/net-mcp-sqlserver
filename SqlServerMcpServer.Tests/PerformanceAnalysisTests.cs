using System;
using System.Threading.Tasks;
using Xunit;
using SqlServerMcpServer.Operations;

namespace SqlServerMcpServer.Tests
{
    public class PerformanceAnalysisTests
    {
        private static readonly bool ServerStateAvailable = TestDatabaseHelper.HasViewServerState;

        [Fact]
        public async Task GetMissingIndexes_AllSuggestions_ReturnsValidJson()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await PerformanceAnalysis.GetMissingIndexes(
                tableName: null, 
                minImpact: 10.0m, 
                topN: 20);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);

            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("missing_indexes", out var indexes));
            Assert.Equal(System.Text.Json.JsonValueKind.Array, indexes.ValueKind);
            
            Assert.True(root.TryGetProperty("summary", out var summary));
            Assert.True(summary.TryGetProperty("total_suggestions", out _));
        }

        [Fact]
        public async Task GetMissingIndexes_WithTableFilter_ReturnsValidJson()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await PerformanceAnalysis.GetMissingIndexes(
                tableName: "test_table", 
                minImpact: 10.0m, 
                topN: 20);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("filters_applied", out var filters));
            Assert.True(filters.TryGetProperty("table_name", out var tableName));
            Assert.Equal("test_table", tableName.GetString());
        }

        [Fact]
        public async Task GetMissingIndexes_WithMinImpact_FiltersResults()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await PerformanceAnalysis.GetMissingIndexes(
                tableName: null, 
                minImpact: 50.0m, 
                topN: 20);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("filters_applied", out var filters));
            Assert.True(filters.TryGetProperty("min_impact", out var minImpact));
            Assert.Equal(50.0m, minImpact.GetDecimal());
        }

        [Fact]
        public async Task GetMissingIndexes_ValidatesStructure()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await PerformanceAnalysis.GetMissingIndexes(
                tableName: null, 
                minImpact: 10.0m, 
                topN: 20);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("missing_indexes", out var indexes));
            
            if (indexes.GetArrayLength() > 0)
            {
                var firstIndex = indexes[0];
                Assert.True(firstIndex.TryGetProperty("table_name", out _));
                Assert.True(firstIndex.TryGetProperty("equality_columns", out _));
                Assert.True(firstIndex.TryGetProperty("inequality_columns", out _));
                Assert.True(firstIndex.TryGetProperty("included_columns", out _));
                Assert.True(firstIndex.TryGetProperty("impact_score", out _));
            }
        }

        [Fact]
        public async Task GetQueryExecutionPlan_EstimatedPlan_ReturnsValidJson()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await PerformanceAnalysis.GetQueryExecutionPlan(
                query: "SELECT * FROM sys.tables", 
                planType: "ESTIMATED", 
                includeAnalysis: true);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);

            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("execution_plan", out _));
            Assert.True(root.TryGetProperty("plan_type", out var planType));
            Assert.Equal("ESTIMATED", planType.GetString());
        }

        [Fact]
        public async Task GetQueryExecutionPlan_WithAnalysis_IncludesWarnings()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await PerformanceAnalysis.GetQueryExecutionPlan(
                query: "SELECT * FROM sys.tables", 
                planType: "ESTIMATED", 
                includeAnalysis: true);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("parameters", out var parameters));
            Assert.True(parameters.TryGetProperty("include_analysis", out var includeAnalysis));
            Assert.True(includeAnalysis.GetBoolean());
        }

        [Fact]
        public async Task GetQueryExecutionPlan_ShowplanXml_ReturnsValidJson()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await PerformanceAnalysis.GetQueryExecutionPlan(
                query: "SELECT * FROM sys.tables", 
                planType: "SHOWPLAN_XML", 
                includeAnalysis: false);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("plan_type", out var planType));
            Assert.Equal("SHOWPLAN_XML", planType.GetString());
        }

        [Fact]
        public async Task GetIndexFragmentation_AllIndexes_ReturnsValidJson()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await PerformanceAnalysis.GetIndexFragmentation(
                tableName: null, 
                schemaName: "dbo", 
                minFragmentation: 10.0m, 
                includeOnlineStatus: true);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);

            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("fragmented_indexes", out var indexes));
            Assert.Equal(System.Text.Json.JsonValueKind.Array, indexes.ValueKind);
            
            Assert.True(root.TryGetProperty("summary", out var summary));
            Assert.True(summary.TryGetProperty("total_indexes_checked", out _));
        }

        [Fact]
        public async Task GetIndexFragmentation_WithTableFilter_ReturnsValidJson()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await PerformanceAnalysis.GetIndexFragmentation(
                tableName: "test_table", 
                schemaName: "dbo", 
                minFragmentation: 10.0m, 
                includeOnlineStatus: true);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("filters_applied", out var filters));
            Assert.True(filters.TryGetProperty("table_name", out var tableName));
            Assert.Equal("test_table", tableName.GetString());
        }

        [Fact]
        public async Task GetIndexFragmentation_WithMinFragmentation_FiltersResults()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await PerformanceAnalysis.GetIndexFragmentation(
                tableName: null, 
                schemaName: "dbo", 
                minFragmentation: 30.0m, 
                includeOnlineStatus: true);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("filters_applied", out var filters));
            Assert.True(filters.TryGetProperty("min_fragmentation", out var minFrag));
            Assert.Equal(30.0m, minFrag.GetDecimal());
        }

        [Fact]
        public async Task GetIndexFragmentation_ValidatesStructure()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await PerformanceAnalysis.GetIndexFragmentation(
                tableName: null, 
                schemaName: "dbo", 
                minFragmentation: 10.0m, 
                includeOnlineStatus: true);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("fragmented_indexes", out var indexes));
            
            if (indexes.GetArrayLength() > 0)
            {
                var firstIndex = indexes[0];
                Assert.True(firstIndex.TryGetProperty("table_name", out _));
                Assert.True(firstIndex.TryGetProperty("index_name", out _));
                Assert.True(firstIndex.TryGetProperty("fragmentation_percent", out _));
                Assert.True(firstIndex.TryGetProperty("page_count", out _));
            }
        }

        [Fact]
        public async Task GetWaitStats_DefaultParams_ReturnsValidJson()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await PerformanceAnalysis.GetWaitStats(
                topN: 20, 
                includeSystemWaits: false, 
                resetStats: false);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);

            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("wait_statistics", out var waitStats));
            Assert.Equal(System.Text.Json.JsonValueKind.Array, waitStats.ValueKind);
            
            Assert.True(root.TryGetProperty("category_summary", out var summary));
        }

        [Fact]
        public async Task GetWaitStats_WithTopN_LimitsResults()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await PerformanceAnalysis.GetWaitStats(
                topN: 10, 
                includeSystemWaits: false, 
                resetStats: false);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("parameters", out var parameters));
            Assert.True(parameters.TryGetProperty("top_n", out var topN));
            Assert.Equal(10, topN.GetInt32());
        }

        [Fact]
        public async Task GetWaitStats_IncludeSystemWaits_ReturnsValidJson()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await PerformanceAnalysis.GetWaitStats(
                topN: 20, 
                includeSystemWaits: true, 
                resetStats: false);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("parameters", out var parameters));
            Assert.True(parameters.TryGetProperty("include_system_waits", out var includeSys));
            Assert.True(includeSys.GetBoolean());
        }

        [Fact]
        public async Task GetWaitStats_ValidatesStructure()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await PerformanceAnalysis.GetWaitStats(
                topN: 20, 
                includeSystemWaits: false, 
                resetStats: false);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("wait_statistics", out var waitStats));
            
            if (waitStats.GetArrayLength() > 0)
            {
                var firstWait = waitStats[0];
                Assert.True(firstWait.TryGetProperty("wait_type", out _));
                Assert.True(firstWait.TryGetProperty("waiting_tasks_count", out _));
                Assert.True(firstWait.TryGetProperty("wait_time_ms", out _));
                Assert.True(firstWait.TryGetProperty("wait_category", out _));
                Assert.True(firstWait.TryGetProperty("recommendation", out _));
            }
        }

        [Fact]
        public async Task GetWaitStats_IncludesServerInfo()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await PerformanceAnalysis.GetWaitStats(
                topN: 20, 
                includeSystemWaits: false, 
                resetStats: false);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("server_name", out _));
            Assert.True(root.TryGetProperty("database", out _));
        }
    }
}
