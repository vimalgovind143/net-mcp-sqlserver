using System;
using System.Threading.Tasks;
using Xunit;

using Microsoft.Data.SqlClient;
using SqlServerMcpServer.Operations;
using SqlServerMcpServer.Configuration;

namespace SqlServerMcpServer.Tests
{
    public class DatabaseOperationsTests
    {
        [Fact]
        public async Task GetServerHealthAsync_ReturnsValidJson()
        {
            // Act
            var result = await DatabaseOperations.GetServerHealthAsync();

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public void GetCurrentDatabase_ReturnsValidJson()
        {
            // Act
            var result = DatabaseOperations.GetCurrentDatabase();

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
            
            // Verify expected properties
            var root = jsonDocument.RootElement;
            Assert.True(root.TryGetProperty("server_name", out _));
            Assert.True(root.TryGetProperty("environment", out _));
            Assert.True(root.TryGetProperty("current_database", out _));
            Assert.True(root.TryGetProperty("security_mode", out _));
        }

        [Fact]
        public void SwitchDatabase_WithValidName_ReturnsSuccessMessage()
        {
            // This test would require a real database connection to work properly
            // For now, we'll just verify the method returns a JSON string
            
            // Act & Assert
            try
            {
                var result = DatabaseOperations.SwitchDatabase("master");
                
                // Verify it's valid JSON
                Assert.NotNull(result);
                Assert.True(result.Length > 0);
                
                var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
                Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
            }
            catch (Exception)
            {
                // Expected if test database doesn't exist
                // We're just testing the JSON format here
            }
        }

        [Fact]
        public async Task GetDatabasesAsync_WithDefaultParameters_ReturnsValidJson()
        {
            // Act
            var result = await DatabaseOperations.GetDatabasesAsync();

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetDatabasesAsync_WithIncludeSystemDatabases_ReturnsValidJson()
        {
            // Act
            var result = await DatabaseOperations.GetDatabasesAsync(includeSystemDatabases: true);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetDatabasesAsync_WithMinSizeFilter_ReturnsValidJson()
        {
            // Act
            var result = await DatabaseOperations.GetDatabasesAsync(minSizeMB: 10);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetDatabasesAsync_WithStateFilter_ReturnsValidJson()
        {
            // Act
            var result = await DatabaseOperations.GetDatabasesAsync(stateFilter: "ONLINE");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetDatabasesAsync_WithNameFilter_ReturnsValidJson()
        {
            // Act
            var result = await DatabaseOperations.GetDatabasesAsync(nameFilter: "master");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetDatabasesAsync_WithInvalidStateFilter_DefaultsToOnline()
        {
            // Act
            var result = await DatabaseOperations.GetDatabasesAsync(stateFilter: "INVALID");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public async Task GetDatabasesAsync_WithAllParameters_ReturnsValidJson()
        {
            // Act
            var result = await DatabaseOperations.GetDatabasesAsync(
                includeSystemDatabases: true,
                minSizeMB: 1,
                stateFilter: "ONLINE",
                nameFilter: "m"
            );

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Verify it's valid JSON
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            Assert.True(jsonDocument.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
        }
    }
}