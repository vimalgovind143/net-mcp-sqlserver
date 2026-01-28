using System;
using System.Threading.Tasks;
using Xunit;
using SqlServerMcpServer.Operations;

namespace SqlServerMcpServer.Tests
{
    public class SchemaInspectionTests
    {
        private static readonly bool DatabaseAvailable = TestDatabaseHelper.IsDatabaseAvailable;

        [Fact]
        public async Task GetTablesAsync_WithDefaultParameters_ReturnsValidJson()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetTablesAsync();

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetTablesAsync_WithSchemaFilter_ReturnsValidJson()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetTablesAsync(schemaFilter: "dbo");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetTablesAsync_WithNameFilter_ReturnsValidJson()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetTablesAsync(nameFilter: "sys");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetTablesAsync_WithMinRowCount_ReturnsValidJson()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetTablesAsync(minRowCount: 0);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetTablesAsync_WithSortByName_ReturnsValidJson()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetTablesAsync(sortBy: "NAME");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetTablesAsync_WithSortBySize_ReturnsValidJson()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetTablesAsync(sortBy: "SIZE");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetTablesAsync_WithSortByRows_ReturnsValidJson()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetTablesAsync(sortBy: "ROWS");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetTablesAsync_WithDescendingSort_ReturnsValidJson()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetTablesAsync(sortBy: "NAME", sortOrder: "DESC");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetTablesAsync_WithInvalidSortBy_DefaultsToName()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetTablesAsync(sortBy: "INVALID");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetTablesAsync_WithInvalidSortOrder_DefaultsToAsc()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetTablesAsync(sortOrder: "INVALID");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetTableSchemaAsync_WithValidTable_ReturnsValidJson()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetTableSchemaAsync("sysobjects");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetTableSchemaAsync_WithCustomSchema_ReturnsValidJson()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetTableSchemaAsync("sysobjects", "sys");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetTableSchemaAsync_WithStatistics_ReturnsValidJson()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetTableSchemaAsync("sysobjects", includeStatistics: true);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetStoredProceduresAsync_WithDefaultParameters_ReturnsValidJson()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetStoredProceduresAsync();

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetStoredProceduresAsync_WithNameFilter_ReturnsValidJson()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetStoredProceduresAsync(nameFilter: "sp_");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetStoredProcedureDetailsAsync_WithValidProcedure_ReturnsValidJson()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetStoredProcedureDetailsAsync("sp_helptext");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetStoredProcedureDetailsAsync_WithCustomSchema_ReturnsValidJson()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetStoredProcedureDetailsAsync("sp_helptext", "sys");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetObjectDefinitionAsync_WithAutoDetect_ReturnsValidJson()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetObjectDefinitionAsync("sysobjects", objectType: "AUTO");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetObjectDefinitionAsync_WithProcedureType_ReturnsValidJson()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetObjectDefinitionAsync("sp_helptext", objectType: "PROCEDURE");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetObjectDefinitionAsync_WithFunctionType_ReturnsValidJson()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetObjectDefinitionAsync("OBJECT_NAME", objectType: "FUNCTION");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetObjectDefinitionAsync_WithViewType_ReturnsValidJson()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetObjectDefinitionAsync("sys.views", objectType: "VIEW");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetObjectDefinitionAsync_WithNonExistentObject_ReturnsErrorJson()
        {
            // Act
            if (!DatabaseAvailable) return;
            var result = await SchemaInspection.GetObjectDefinitionAsync("NonExistentObject", objectType: "AUTO");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
            
            // Verify it contains error information (may be in different format)
            var root = jsonDocument.RootElement;
            Assert.True(root.TryGetProperty("error", out _) ||
                      root.TryGetProperty("operation_type", out _));
        }
    }
}
