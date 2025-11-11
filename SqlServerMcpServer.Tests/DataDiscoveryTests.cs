using System;
using System.Threading.Tasks;
using Xunit;
using SqlServerMcpServer.Operations;

namespace SqlServerMcpServer.Tests
{
    public class DataDiscoveryTests
    {
        [Fact]
        public async Task SearchTableData_AllTables_ReturnsValidJson()
        {
            // Act
            var result = await DataDiscovery.SearchTableData(
                searchPattern: "%test%", 
                tableName: null, 
                schemaName: "dbo", 
                columnNames: null, 
                maxRows: 10, 
                maxTables: 10);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);

            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("search_results", out var results));
            Assert.Equal(System.Text.Json.JsonValueKind.Array, results.ValueKind);
            
            Assert.True(root.TryGetProperty("summary", out var summary));
            Assert.True(summary.TryGetProperty("search_pattern", out var pattern));
            Assert.Equal("%test%", pattern.GetString());
        }

        [Fact]
        public async Task SearchTableData_WithTableFilter_ReturnsValidJson()
        {
            // Act
            var result = await DataDiscovery.SearchTableData(
                searchPattern: "%test%", 
                tableName: "specific_table", 
                schemaName: "dbo", 
                columnNames: null, 
                maxRows: 10, 
                maxTables: 10);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("filters_applied", out var filters));
            Assert.True(filters.TryGetProperty("table_name", out var tableName));
            Assert.Equal("specific_table", tableName.GetString());
        }

        [Fact]
        public async Task SearchTableData_WithColumnFilter_ReturnsValidJson()
        {
            // Act
            var result = await DataDiscovery.SearchTableData(
                searchPattern: "%test%", 
                tableName: null, 
                schemaName: "dbo", 
                columnNames: "col1,col2", 
                maxRows: 10, 
                maxTables: 10);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("filters_applied", out var filters));
            Assert.True(filters.TryGetProperty("column_names", out var columnNames));
        }

        [Fact]
        public async Task SearchTableData_WithMaxRows_LimitsResults()
        {
            // Act
            var result = await DataDiscovery.SearchTableData(
                searchPattern: "%test%", 
                tableName: null, 
                schemaName: "dbo", 
                columnNames: null, 
                maxRows: 5, 
                maxTables: 10);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("summary", out var summary));
            Assert.True(summary.TryGetProperty("max_rows_per_table", out var maxRows));
            Assert.Equal(5, maxRows.GetInt32());
        }

        [Fact]
        public async Task SearchTableData_ValidatesStructure()
        {
            // Act
            var result = await DataDiscovery.SearchTableData(
                searchPattern: "%test%", 
                tableName: null, 
                schemaName: "dbo", 
                columnNames: null, 
                maxRows: 10, 
                maxTables: 10);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("search_results", out var results));
            
            if (results.GetArrayLength() > 0)
            {
                var firstResult = results[0];
                Assert.True(firstResult.TryGetProperty("table_name", out _));
                Assert.True(firstResult.TryGetProperty("schema_name", out _));
                Assert.True(firstResult.TryGetProperty("column_name", out _));
                Assert.True(firstResult.TryGetProperty("match_count", out _));
            }
        }

        [Fact]
        public async Task GetColumnStatistics_SingleColumn_ReturnsValidJson()
        {
            // Act
            var result = await DataDiscovery.GetColumnStatistics(
                tableName: "sys.tables", 
                schemaName: "sys", 
                columnName: "name", 
                includeHistogram: false, 
                sampleSize: 10000);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);

            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("statistics", out var statistics));
            Assert.Equal(System.Text.Json.JsonValueKind.Array, statistics.ValueKind);
        }

        [Fact]
        public async Task GetColumnStatistics_AllColumns_ReturnsValidJson()
        {
            // Act
            var result = await DataDiscovery.GetColumnStatistics(
                tableName: "sys.tables", 
                schemaName: "sys", 
                columnName: null, 
                includeHistogram: false, 
                sampleSize: 10000);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("filters_applied", out var filters));
            Assert.True(filters.TryGetProperty("column_name", out var columnName));
            Assert.Equal(System.Text.Json.JsonValueKind.Null, columnName.ValueKind);
        }

        [Fact]
        public async Task GetColumnStatistics_WithHistogram_ReturnsValidJson()
        {
            // Act
            var result = await DataDiscovery.GetColumnStatistics(
                tableName: "sys.tables", 
                schemaName: "sys", 
                columnName: "name", 
                includeHistogram: true, 
                sampleSize: 10000);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("filters_applied", out var filters));
            Assert.True(filters.TryGetProperty("include_histogram", out var includeHistogram));
            Assert.True(includeHistogram.GetBoolean());
        }

        [Fact]
        public async Task GetColumnStatistics_ValidatesStructure()
        {
            // Act
            var result = await DataDiscovery.GetColumnStatistics(
                tableName: "sys.tables", 
                schemaName: "sys", 
                columnName: "name", 
                includeHistogram: false, 
                sampleSize: 10000);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("statistics", out var statistics));
            
            if (statistics.GetArrayLength() > 0)
            {
                var firstStat = statistics[0];
                Assert.True(firstStat.TryGetProperty("column_name", out _));
                Assert.True(firstStat.TryGetProperty("data_type", out _));
            }
        }

        [Fact]
        public async Task FindColumnsByDataType_SpecificType_ReturnsValidJson()
        {
            // Act
            var result = await DataDiscovery.FindColumnsByDataType(
                dataType: "int", 
                schemaName: null, 
                tableName: null, 
                includeNullable: true, 
                includeNotNullable: true, 
                includeIdentity: true, 
                includeComputed: false);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);

            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("columns", out var columns));
            Assert.Equal(System.Text.Json.JsonValueKind.Array, columns.ValueKind);
            
            Assert.True(root.TryGetProperty("summary", out var summary));
            Assert.True(summary.TryGetProperty("total_columns", out _));
        }

        [Fact]
        public async Task FindColumnsByDataType_WithSchemaFilter_ReturnsValidJson()
        {
            // Act
            var result = await DataDiscovery.FindColumnsByDataType(
                dataType: "varchar", 
                schemaName: "dbo", 
                tableName: null, 
                includeNullable: true, 
                includeNotNullable: true, 
                includeIdentity: true, 
                includeComputed: false);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("filters_applied", out var filters));
            Assert.True(filters.TryGetProperty("schema_name", out var schemaName));
            Assert.Equal("dbo", schemaName.GetString());
        }

        [Fact]
        public async Task FindColumnsByDataType_OnlyNullable_ReturnsValidJson()
        {
            // Act
            var result = await DataDiscovery.FindColumnsByDataType(
                dataType: "int", 
                schemaName: null, 
                tableName: null, 
                includeNullable: true, 
                includeNotNullable: false, 
                includeIdentity: true, 
                includeComputed: false);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("filters_applied", out var filters));
            Assert.True(filters.TryGetProperty("include_nullable", out var includeNullable));
            Assert.True(includeNullable.GetBoolean());
            Assert.True(filters.TryGetProperty("include_not_nullable", out var includeNotNullable));
            Assert.False(includeNotNullable.GetBoolean());
        }

        [Fact]
        public async Task FindColumnsByDataType_ValidatesStructure()
        {
            // Act
            var result = await DataDiscovery.FindColumnsByDataType(
                dataType: "int", 
                schemaName: null, 
                tableName: null, 
                includeNullable: true, 
                includeNotNullable: true, 
                includeIdentity: true, 
                includeComputed: false);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("columns", out var columns));
            
            if (columns.GetArrayLength() > 0)
            {
                var firstColumn = columns[0];
                Assert.True(firstColumn.TryGetProperty("schema_name", out _));
                Assert.True(firstColumn.TryGetProperty("table_name", out _));
                Assert.True(firstColumn.TryGetProperty("column_name", out _));
                Assert.True(firstColumn.TryGetProperty("data_type", out _));
                Assert.True(firstColumn.TryGetProperty("is_nullable", out _));
            }
        }

        [Fact]
        public async Task FindTablesWithColumn_ExactMatch_ReturnsValidJson()
        {
            // Act
            var result = await DataDiscovery.FindTablesWithColumn(
                columnName: "name", 
                schemaName: null, 
                exactMatch: true, 
                includeSystemTables: false, 
                includeRowCount: true, 
                maxResults: 100);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);

            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("tables", out var tables));
            Assert.Equal(System.Text.Json.JsonValueKind.Array, tables.ValueKind);
            
            Assert.True(root.TryGetProperty("summary", out var summary));
            Assert.True(summary.TryGetProperty("total_tables", out _));
        }

        [Fact]
        public async Task FindTablesWithColumn_WildcardSearch_ReturnsValidJson()
        {
            // Act
            var result = await DataDiscovery.FindTablesWithColumn(
                columnName: "id", 
                schemaName: null, 
                exactMatch: false, 
                includeSystemTables: false, 
                includeRowCount: true, 
                maxResults: 100);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("filters_applied", out var filters));
            Assert.True(filters.TryGetProperty("exact_match", out var exactMatch));
            Assert.False(exactMatch.GetBoolean());
        }

        [Fact]
        public async Task FindTablesWithColumn_IncludeSystemTables_ReturnsValidJson()
        {
            // Act
            var result = await DataDiscovery.FindTablesWithColumn(
                columnName: "name", 
                schemaName: null, 
                exactMatch: false, 
                includeSystemTables: true, 
                includeRowCount: false, 
                maxResults: 100);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("filters_applied", out var filters));
            Assert.True(filters.TryGetProperty("include_system_tables", out var includeSys));
            Assert.True(includeSys.GetBoolean());
        }

        [Fact]
        public async Task FindTablesWithColumn_ValidatesStructure()
        {
            // Act
            var result = await DataDiscovery.FindTablesWithColumn(
                columnName: "name", 
                schemaName: null, 
                exactMatch: false, 
                includeSystemTables: false, 
                includeRowCount: true, 
                maxResults: 100);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("tables", out var tables));
            
            if (tables.GetArrayLength() > 0)
            {
                var firstTable = tables[0];
                Assert.True(firstTable.TryGetProperty("schema_name", out _));
                Assert.True(firstTable.TryGetProperty("table_name", out _));
                Assert.True(firstTable.TryGetProperty("columns", out var columns));
                Assert.Equal(System.Text.Json.JsonValueKind.Array, columns.ValueKind);
            }
        }

        [Fact]
        public async Task FindTablesWithColumn_IncludesServerInfo()
        {
            // Act
            var result = await DataDiscovery.FindTablesWithColumn(
                columnName: "name", 
                schemaName: null, 
                exactMatch: false, 
                includeSystemTables: false, 
                includeRowCount: true, 
                maxResults: 100);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("server_name", out _));
            Assert.True(root.TryGetProperty("database", out _));
        }
    }
}
