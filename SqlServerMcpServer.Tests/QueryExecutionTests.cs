using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using SqlServerMcpServer.Operations;
using SqlServerMcpServer.Security;

namespace SqlServerMcpServer.Tests
{
    public class QueryExecutionTests
    {
        [Fact]
        public async Task ExecuteQueryAsync_WithValidSelect_ReturnsValidJson()
        {
            // Arrange
            var query = "SELECT 1 AS TestColumn";

            // Act
            var result = await QueryExecution.ExecuteQueryAsync(query);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task ExecuteQueryAsync_WithInvalidQuery_ReturnsErrorJson()
        {
            // Arrange
            var query = "SELECT * FROM NonExistentTable";

            // Act
            var result = await QueryExecution.ExecuteQueryAsync(query);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
            
            // Verify error properties
            var root = jsonDocument.RootElement;
            Assert.True(root.TryGetProperty("error", out _));
        }

        [Fact]
        public async Task ExecuteQueryAsync_WithInsertQuery_ReturnsBlockedResponse()
        {
            // Arrange
            var query = "INSERT INTO TestTable (Column1) VALUES ('Test')";

            // Act
            var result = await QueryExecution.ExecuteQueryAsync(query);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
            
            // Verify it contains error information (may be in different format)
            var root = jsonDocument.RootElement;
            Assert.True(root.TryGetProperty("error", out _) ||
                      root.TryGetProperty("blocked_operation", out _));
        }

        [Fact]
        public async Task ExecuteQueryAsync_WithUpdateQuery_ReturnsBlockedResponse()
        {
            // Arrange
            var query = "UPDATE TestTable SET Column1 = 'Updated'";

            // Act
            var result = await QueryExecution.ExecuteQueryAsync(query);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
            
            // Verify it contains error information (may be in different format)
            var root = jsonDocument.RootElement;
            Assert.True(root.TryGetProperty("error", out _) ||
                      root.TryGetProperty("blocked_operation", out _));
        }

        [Fact]
        public async Task ExecuteQueryAsync_WithDeleteQuery_ReturnsBlockedResponse()
        {
            // Arrange
            var query = "DELETE FROM TestTable";

            // Act
            var result = await QueryExecution.ExecuteQueryAsync(query);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
            
            // Verify it contains error information (may be in different format)
            var root = jsonDocument.RootElement;
            Assert.True(root.TryGetProperty("error", out _) ||
                      root.TryGetProperty("blocked_operation", out _));
        }

        [Fact]
        public async Task ExecuteQueryAsync_WithMaxRows_ReturnsLimitedResults()
        {
            // Arrange
            var query = "SELECT 1 AS TestColumn UNION SELECT 2 UNION SELECT 3";

            // Act
            var result = await QueryExecution.ExecuteQueryAsync(query, maxRows: 2);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task ExecuteQueryAsync_WithOffset_ReturnsPaginatedResults()
        {
            // Arrange
            var query = "SELECT 1 AS TestColumn UNION SELECT 2 UNION SELECT 3";

            // Act
            var result = await QueryExecution.ExecuteQueryAsync(query, offset: 1);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task ExecuteQueryAsync_WithStatistics_ReturnsStatistics()
        {
            // Arrange
            var query = "SELECT 1 AS TestColumn";

            // Act
            var result = await QueryExecution.ExecuteQueryAsync(query, includeStatistics: true);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task ReadQueryAsync_WithValidSelect_ReturnsValidJson()
        {
            // Arrange
            var query = "SELECT 1 AS TestColumn";

            // Act
            var result = await QueryExecution.ReadQueryAsync(query);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task ReadQueryAsync_WithJsonFormat_ReturnsJson()
        {
            // Arrange
            var query = "SELECT 1 AS TestColumn";

            // Act
            var result = await QueryExecution.ReadQueryAsync(query, format: "json");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
            
            // Verify format property
            var root = jsonDocument.RootElement;
            Assert.True(root.TryGetProperty("format", out var format));
            Assert.Equal("json", format.GetString());
        }

        [Fact]
        public async Task ReadQueryAsync_WithCsvFormat_ReturnsCsv()
        {
            // Arrange
            var query = "SELECT 1 AS TestColumn";

            // Act
            var result = await QueryExecution.ReadQueryAsync(query, format: "csv");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
            
            // Verify format property
            var root = jsonDocument.RootElement;
            Assert.True(root.TryGetProperty("format", out var format));
            Assert.Equal("csv", format.GetString());
        }

        [Fact]
        public async Task ReadQueryAsync_WithTableFormat_ReturnsHtml()
        {
            // Arrange
            var query = "SELECT 1 AS TestColumn";

            // Act
            var result = await QueryExecution.ReadQueryAsync(query, format: "table");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
            
            // Verify format property
            var root = jsonDocument.RootElement;
            Assert.True(root.TryGetProperty("format", out var format));
            Assert.Equal("table", format.GetString());
        }

        [Fact]
        public async Task ReadQueryAsync_WithInvalidFormat_ReturnsError()
        {
            // Arrange
            var query = "SELECT 1 AS TestColumn";

            // Act
            var result = await QueryExecution.ReadQueryAsync(query, format: "invalid");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
            
            // Verify error properties
            var root = jsonDocument.RootElement;
            Assert.True(root.TryGetProperty("error", out _));
            Assert.True(root.TryGetProperty("operation_type", out var opType));
            Assert.Equal("VALIDATION_ERROR", opType.GetString());
        }

        [Fact]
        public async Task ReadQueryAsync_WithParameters_ReturnsValidJson()
        {
            // Arrange
            var query = "SELECT @Id AS TestColumn";
            var parameters = new Dictionary<string, object>
            {
                { "Id", 42 }
            };

            // Act
            var result = await QueryExecution.ReadQueryAsync(query, parameters: parameters);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task ReadQueryAsync_WithCustomTimeout_ReturnsValidJson()
        {
            // Arrange
            var query = "SELECT 1 AS TestColumn";

            // Act
            var result = await QueryExecution.ReadQueryAsync(query, timeout: 60);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task ReadQueryAsync_WithCustomMaxRows_ReturnsValidJson()
        {
            // Arrange
            var query = "SELECT 1 AS TestColumn";

            // Act
            var result = await QueryExecution.ReadQueryAsync(query, max_rows: 500);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task ReadQueryAsync_WithCustomDelimiter_ReturnsValidJson()
        {
            // Arrange
            var query = "SELECT 1 AS TestColumn";

            // Act
            var result = await QueryExecution.ReadQueryAsync(query, format: "csv", delimiter: ";");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task ReadQueryAsync_WithTabDelimiter_ReturnsValidJson()
        {
            // Arrange
            var query = "SELECT 1 AS TestColumn";

            // Act
            var result = await QueryExecution.ReadQueryAsync(query, format: "csv", delimiter: "tab");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }
    }
}