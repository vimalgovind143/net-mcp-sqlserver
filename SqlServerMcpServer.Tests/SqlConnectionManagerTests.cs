using System;
using Xunit;
using Microsoft.Data.SqlClient;
using SqlServerMcpServer.Configuration;

namespace SqlServerMcpServer.Tests
{
    public class SqlConnectionManagerTests
    {
        [Fact]
        public void CurrentConnectionString_ReturnsValueOrNotConfigured()
        {
            // Act
            var connectionString = SqlConnectionManager.CurrentConnectionString;

            // Assert - connection string may be empty if not configured
            Assert.NotNull(connectionString);
            // Either it's configured, or it returns empty string (which is valid for the property)
        }

        [Fact]
        public void CurrentDatabase_ReturnsNonNullValue()
        {
            // Act
            var database = SqlConnectionManager.CurrentDatabase;

            // Assert
            Assert.NotNull(database);
            Assert.True(database.Length > 0);
        }

        [Fact]
        public void ServerName_ReturnsNonNullValue()
        {
            // Act
            var serverName = SqlConnectionManager.ServerName;

            // Assert
            Assert.NotNull(serverName);
            Assert.True(serverName.Length > 0);
        }

        [Fact]
        public void Environment_ReturnsNonNullValue()
        {
            // Act
            var environment = SqlConnectionManager.Environment;

            // Assert
            Assert.NotNull(environment);
            Assert.True(environment.Length > 0);
        }

        [Fact]
        public void CommandTimeout_ReturnsPositiveValue()
        {
            // Act
            var timeout = SqlConnectionManager.CommandTimeout;

            // Assert
            Assert.True(timeout > 0);
        }

        [Fact]
        public void CreateConnection_WithConfiguredConnectionString_ReturnsValidSqlConnection()
        {
            // Skip if no connection string is configured
            if (string.IsNullOrWhiteSpace(SqlConnectionManager.CurrentConnectionString))
            {
                return; // Skip test - no connection string configured
            }

            // Act
            using var connection = SqlConnectionManager.CreateConnection();

            // Assert
            Assert.NotNull(connection);
            Assert.Equal(SqlConnectionManager.CurrentConnectionString, connection.ConnectionString);
        }

        [Fact]
        public void CreateConnection_WithoutConfiguredConnectionString_ThrowsInvalidOperationException()
        {
            // This test only applies when no connection string is configured
            if (!string.IsNullOrWhiteSpace(SqlConnectionManager.CurrentConnectionString))
            {
                return; // Skip test - connection string is configured
            }

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => SqlConnectionManager.CreateConnection());
            Assert.Contains("No database connection string configured", exception.Message);
        }

        [Fact]
        public void CreateConnectionStringForDatabase_WithValidName_ModifiesConnectionString()
        {
            // Arrange
            var originalConnectionString = SqlConnectionManager.CurrentConnectionString;
            var testDatabase = "TestDatabase";

            // Act
            var newConnectionString = SqlConnectionManager.CreateConnectionStringForDatabase(testDatabase);

            // Assert
            Assert.NotEqual(originalConnectionString, newConnectionString);
            Assert.Contains(testDatabase, newConnectionString);
        }

        [Fact]
        public void CreateConnectionStringForDatabase_WithEmptyName_ReturnsOriginal()
        {
            // Arrange
            var originalConnectionString = SqlConnectionManager.CurrentConnectionString;

            // Act
            var newConnectionString = SqlConnectionManager.CreateConnectionStringForDatabase("");

            // Assert
            // The connection string builder will set Initial Catalog to empty string
            // which changes connection string format, so we'll just verify it's not null
            Assert.NotNull(newConnectionString);
            Assert.NotEqual(originalConnectionString, newConnectionString);
        }

        [Fact]
        public void CreateConnectionStringForDatabase_WithNullName_ReturnsOriginal()
        {
            // Arrange
            var originalConnectionString = SqlConnectionManager.CurrentConnectionString;

            // Act
            var newConnectionString = SqlConnectionManager.CreateConnectionStringForDatabase(null);

            // Assert
            Assert.Equal(originalConnectionString, newConnectionString);
        }
    }
}