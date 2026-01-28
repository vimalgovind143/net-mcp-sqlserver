using System;
using System.Threading.Tasks;
using Xunit;
using SqlServerMcpServer.Operations;

namespace SqlServerMcpServer.Tests
{
    public class DiagnosticsTests
    {
        private static readonly bool ServerStateAvailable = TestDatabaseHelper.HasViewServerState;

        [Fact]
        public async Task GetDatabaseSize_WithTableBreakdown_ReturnsValidJson()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await Diagnostics.GetDatabaseSize(includeTableBreakdown: true, topN: 10);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);

            // Verify it's valid JSON with expected structure
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("summary", out var summary));
            Assert.True(summary.TryGetProperty("total_size_mb", out _));
            Assert.True(summary.TryGetProperty("data_size_mb", out _));
            Assert.True(summary.TryGetProperty("log_size_mb", out _));
            
            Assert.True(root.TryGetProperty("tables", out var tables));
            Assert.Equal(System.Text.Json.JsonValueKind.Array, tables.ValueKind);
        }

        [Fact]
        public async Task GetDatabaseSize_WithoutTableBreakdown_ReturnsValidJson()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await Diagnostics.GetDatabaseSize(includeTableBreakdown: false, topN: 0);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);

            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("summary", out var summary));
            Assert.True(root.TryGetProperty("tables", out var tables));
            Assert.Equal(System.Text.Json.JsonValueKind.Array, tables.ValueKind);
            Assert.Equal(0, tables.GetArrayLength());
        }

        [Fact]
        public async Task GetDatabaseSize_WithTopN_LimitsResults()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await Diagnostics.GetDatabaseSize(includeTableBreakdown: true, topN: 5);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("parameters", out var parameters));
            Assert.True(parameters.TryGetProperty("top_n", out var topN));
            Assert.Equal(5, topN.GetInt32());
        }

        [Fact]
        public async Task GetBackupHistory_ForCurrentDatabase_ReturnsValidJson()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await Diagnostics.GetBackupHistory(databaseName: null, topN: 20);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);

            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("backups", out var backups));
            Assert.Equal(System.Text.Json.JsonValueKind.Array, backups.ValueKind);
            
            Assert.True(root.TryGetProperty("parameters", out var parameters));
            Assert.True(parameters.TryGetProperty("top_n", out var topN));
            Assert.Equal(20, topN.GetInt32());
        }

        [Fact]
        public async Task GetBackupHistory_WithDatabaseName_ReturnsValidJson()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await Diagnostics.GetBackupHistory(databaseName: "master", topN: 10);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);

            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("backups", out var backups));
            Assert.Equal(System.Text.Json.JsonValueKind.Array, backups.ValueKind);
            
            Assert.True(root.TryGetProperty("parameters", out var parameters));
            Assert.True(parameters.TryGetProperty("database_name", out var dbName));
            Assert.Equal("master", dbName.GetString());
        }

        [Fact]
        public async Task GetBackupHistory_ValidatesBackupTypeFormat()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await Diagnostics.GetBackupHistory(databaseName: null, topN: 5);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("backups", out var backups));
            
            // Check first backup record if exists
            if (backups.GetArrayLength() > 0)
            {
                var firstBackup = backups[0];
                Assert.True(firstBackup.TryGetProperty("backup_type", out var backupType));
                var type = backupType.GetString();
                Assert.Contains(type, new[] { "FULL", "DIFFERENTIAL", "LOG" });
                
                Assert.True(firstBackup.TryGetProperty("backup_size_mb", out _));
                Assert.True(firstBackup.TryGetProperty("duration_seconds", out _));
            }
        }

        [Fact]
        public async Task GetErrorLog_ReturnsValidJson()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await Diagnostics.GetErrorLog(topN: 50);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);

            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("source", out var source));
            Assert.Equal("system_health ring_buffer", source.GetString());
            
            Assert.True(root.TryGetProperty("events", out var events));
            Assert.Equal(System.Text.Json.JsonValueKind.Array, events.ValueKind);
            
            Assert.True(root.TryGetProperty("parameters", out var parameters));
            Assert.True(parameters.TryGetProperty("top_n", out var topN));
            Assert.Equal(50, topN.GetInt32());
        }

        [Fact]
        public async Task GetErrorLog_WithTopN_LimitsResults()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await Diagnostics.GetErrorLog(topN: 10);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("events", out var events));
            Assert.True(events.GetArrayLength() <= 10);
        }

        [Fact]
        public async Task GetErrorLog_ValidatesEventStructure()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await Diagnostics.GetErrorLog(topN: 100);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("events", out var events));
            
            // Check first event if exists
            if (events.GetArrayLength() > 0)
            {
                var firstEvent = events[0];
                Assert.True(firstEvent.TryGetProperty("event_name", out _));
                Assert.True(firstEvent.TryGetProperty("timestamp", out _));
                Assert.True(firstEvent.TryGetProperty("event_xml", out _));
            }
        }

        [Fact]
        public async Task GetDatabaseSize_IncludesServerInfo()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await Diagnostics.GetDatabaseSize();

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("server_name", out _));
            Assert.True(root.TryGetProperty("environment", out _));
            Assert.True(root.TryGetProperty("database", out _));
        }

        [Fact]
        public async Task GetBackupHistory_IncludesServerInfo()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await Diagnostics.GetBackupHistory();

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("server_name", out _));
            Assert.True(root.TryGetProperty("environment", out _));
            Assert.True(root.TryGetProperty("database", out _));
        }

        [Fact]
        public async Task GetErrorLog_IncludesServerInfo()
        {
            // Act
            if (!ServerStateAvailable) return;
            var result = await Diagnostics.GetErrorLog();

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("server_name", out _));
            Assert.True(root.TryGetProperty("environment", out _));
            Assert.True(root.TryGetProperty("database", out _));
        }
    }
}
