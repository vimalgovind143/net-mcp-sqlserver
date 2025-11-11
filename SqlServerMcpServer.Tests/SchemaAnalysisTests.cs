using System;
using System.Threading.Tasks;
using Xunit;
using SqlServerMcpServer.Operations;

namespace SqlServerMcpServer.Tests
{
    public class SchemaAnalysisTests
    {
        [Fact]
        public async Task GetTableRelationships_AllRelationships_ReturnsValidJson()
        {
            // Act
            var result = await SchemaAnalysis.GetTableRelationships(
                tableName: null, 
                schemaName: "dbo", 
                includeReferencedBy: true, 
                includeReferences: true);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);

            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("relationships", out var relationships));
            Assert.Equal(System.Text.Json.JsonValueKind.Array, relationships.ValueKind);
            
            Assert.True(root.TryGetProperty("summary", out var summary));
            Assert.True(summary.TryGetProperty("total_relationships", out _));
        }

        [Fact]
        public async Task GetTableRelationships_WithTableFilter_ReturnsValidJson()
        {
            // Act
            var result = await SchemaAnalysis.GetTableRelationships(
                tableName: "test_table", 
                schemaName: "dbo", 
                includeReferencedBy: true, 
                includeReferences: true);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("filters_applied", out var filters));
            Assert.True(filters.TryGetProperty("table_name", out var tableName));
            Assert.Equal("test_table", tableName.GetString());
        }

        [Fact]
        public async Task GetTableRelationships_OnlyReferences_ReturnsValidJson()
        {
            // Act
            var result = await SchemaAnalysis.GetTableRelationships(
                tableName: null, 
                schemaName: "dbo", 
                includeReferencedBy: false, 
                includeReferences: true);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("filters_applied", out var filters));
            Assert.True(filters.TryGetProperty("include_references", out var includeReferences));
            Assert.True(includeReferences.GetBoolean());
            Assert.True(filters.TryGetProperty("include_referenced_by", out var includeReferencedBy));
            Assert.False(includeReferencedBy.GetBoolean());
        }

        [Fact]
        public async Task GetTableRelationships_ValidatesRelationshipStructure()
        {
            // Act
            var result = await SchemaAnalysis.GetTableRelationships(
                tableName: null, 
                schemaName: "dbo", 
                includeReferencedBy: true, 
                includeReferences: true);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("relationships", out var relationships));
            
            if (relationships.GetArrayLength() > 0)
            {
                var firstRel = relationships[0];
                Assert.True(firstRel.TryGetProperty("foreign_key_name", out _));
                Assert.True(firstRel.TryGetProperty("parent_table", out _));
                Assert.True(firstRel.TryGetProperty("referenced_table", out _));
                Assert.True(firstRel.TryGetProperty("columns", out _));
            }
        }

        [Fact]
        public async Task GetTableRelationships_IncludesServerInfo()
        {
            // Act
            var result = await SchemaAnalysis.GetTableRelationships(
                tableName: null, 
                schemaName: "dbo", 
                includeReferencedBy: true, 
                includeReferences: true);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("server_name", out _));
            Assert.True(root.TryGetProperty("database", out _));
        }

        [Fact]
        public async Task GetIndexInformation_AllIndexes_ReturnsValidJson()
        {
            // Act
            var result = await SchemaAnalysis.GetIndexInformation(
                tableName: null, 
                schemaName: "dbo", 
                includeStatistics: true);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);

            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("indexes", out var indexes));
            Assert.Equal(System.Text.Json.JsonValueKind.Array, indexes.ValueKind);
            
            Assert.True(root.TryGetProperty("summary", out var summary));
            Assert.True(summary.TryGetProperty("total_indexes", out _));
        }

        [Fact]
        public async Task GetIndexInformation_WithTableFilter_ReturnsValidJson()
        {
            // Act
            var result = await SchemaAnalysis.GetIndexInformation(
                tableName: "test_table", 
                schemaName: "dbo", 
                includeStatistics: true);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("filters_applied", out var filters));
            Assert.True(filters.TryGetProperty("table_name", out var tableName));
            Assert.Equal("test_table", tableName.GetString());
        }

        [Fact]
        public async Task GetIndexInformation_WithoutStatistics_ReturnsValidJson()
        {
            // Act
            var result = await SchemaAnalysis.GetIndexInformation(
                tableName: null, 
                schemaName: "dbo", 
                includeStatistics: false);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("filters_applied", out var filters));
            Assert.True(filters.TryGetProperty("include_statistics", out var includeStats));
            Assert.False(includeStats.GetBoolean());
        }

        [Fact]
        public async Task GetIndexInformation_ValidatesIndexStructure()
        {
            // Act
            var result = await SchemaAnalysis.GetIndexInformation(
                tableName: null, 
                schemaName: "dbo", 
                includeStatistics: true);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("indexes", out var indexes));
            
            if (indexes.GetArrayLength() > 0)
            {
                var firstIndex = indexes[0];
                Assert.True(firstIndex.TryGetProperty("index_name", out _));
                Assert.True(firstIndex.TryGetProperty("index_type", out _));
                Assert.True(firstIndex.TryGetProperty("is_unique", out _));
                Assert.True(firstIndex.TryGetProperty("is_primary_key", out _));
            }
        }

        [Fact]
        public async Task GetIndexInformation_IncludesServerInfo()
        {
            // Act
            var result = await SchemaAnalysis.GetIndexInformation(
                tableName: null, 
                schemaName: "dbo", 
                includeStatistics: true);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("server_name", out _));
            Assert.True(root.TryGetProperty("database", out _));
        }
    }
}
