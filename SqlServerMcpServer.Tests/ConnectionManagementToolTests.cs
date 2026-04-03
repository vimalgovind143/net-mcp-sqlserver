using System;
using System.Text.Json;
using FluentAssertions;
using SqlServerMcpServer.Configuration;
using SqlServerMcpServer.Operations;
using Xunit;

namespace SqlServerMcpServer.Tests
{
    /// <summary>
    /// Unit tests for the ConnectionManagement operation class (GetConnections, AddConnection, SwitchConnection).
    /// These tests verify JSON response shapes and error-handling without requiring a live SQL Server.
    /// </summary>
    public class ConnectionManagementToolTests
    {
        // ── GetConnections ───────────────────────────────────────────────────

        [Fact]
        public void GetConnections_ReturnsValidJson()
        {
            var json = ConnectionManagement.GetConnections();

            json.Should().NotBeNullOrWhiteSpace();
            var doc = JsonDocument.Parse(json);
            doc.RootElement.TryGetProperty("data", out var data).Should().BeTrue();
            data.TryGetProperty("connections", out _).Should().BeTrue();
            data.TryGetProperty("active_connection", out _).Should().BeTrue();
            data.TryGetProperty("total_connections", out var total).Should().BeTrue();
            total.GetInt32().Should().BeGreaterThan(0);
        }

        [Fact]
        public void GetConnections_ContainsDefaultConnection()
        {
            var json = ConnectionManagement.GetConnections();
            var doc  = JsonDocument.Parse(json);
            var connections = doc.RootElement.GetProperty("data").GetProperty("connections");

            bool foundDefault = false;
            foreach (var conn in connections.EnumerateArray())
            {
                if (conn.GetProperty("name").GetString() == "default")
                {
                    foundDefault = true;
                    break;
                }
            }
            foundDefault.Should().BeTrue("the 'default' connection must always be present");
        }

        [Fact]
        public void GetConnections_ResponseContainsConnectionName()
        {
            var json = ConnectionManagement.GetConnections();
            var doc  = JsonDocument.Parse(json);
            doc.RootElement.TryGetProperty("connection_name", out _).Should().BeTrue();
        }

        // ── AddConnection ────────────────────────────────────────────────────

        [Fact]
        public async Task AddConnection_EmptyName_ReturnsErrorJson()
        {
            var json = await ConnectionManagement.AddConnectionAsync("", "Server=x;Database=d;User Id=sa;Password=p;");
            var doc  = JsonDocument.Parse(json);

            doc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
            error.GetProperty("message").GetString().Should().Contain("empty");
        }

        [Fact]
        public async Task AddConnection_EmptyConnectionString_ReturnsErrorJson()
        {
            var json = await ConnectionManagement.AddConnectionAsync("myconn", "");
            var doc  = JsonDocument.Parse(json);

            doc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
            error.GetProperty("message").GetString().Should().Contain("empty");
        }

        [Fact]
        public async Task AddConnection_BadConnectionString_ReturnsErrorJson()
        {
            // A syntactically valid connection string but pointing to a non-existent server
            var json = await ConnectionManagement.AddConnectionAsync(
                "bad-server",
                "Server=192.0.2.1,1433;Database=d;User Id=sa;Password=p;Connect Timeout=2;TrustServerCertificate=true;");

            var doc = JsonDocument.Parse(json);
            // Should return an error (connectivity test failed)
            doc.RootElement.TryGetProperty("error", out _).Should().BeTrue(
                because: "connecting to 192.0.2.1 should fail and return an error response");
        }

        // ── SwitchConnection ──────────────────────────────────────────────────

        [Fact]
        public void SwitchConnection_EmptyName_ReturnsErrorJson()
        {
            var json = ConnectionManagement.SwitchConnection("");
            var doc  = JsonDocument.Parse(json);

            doc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
            error.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public void SwitchConnection_NonExistentName_ReturnsErrorJsonWithAvailableConnections()
        {
            var json = ConnectionManagement.SwitchConnection("__does_not_exist_xyz__");
            var doc  = JsonDocument.Parse(json);

            doc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
            var fixes = error.GetProperty("suggested_fixes");
            bool foundAvailableHint = false;
            foreach (var fix in fixes.EnumerateArray())
            {
                if (fix.GetString()?.Contains("default") == true)
                {
                    foundAvailableHint = true;
                    break;
                }
            }
            foundAvailableHint.Should().BeTrue("error should list available connection names");
        }

        [Fact]
        public void SwitchConnection_ToDefault_Succeeds()
        {
            // "default" is always present
            var json = ConnectionManagement.SwitchConnection("default");
            var doc  = JsonDocument.Parse(json);

            doc.RootElement.TryGetProperty("error", out _).Should().BeFalse(
                because: "switching to 'default' must succeed");
            doc.RootElement.GetProperty("data").GetProperty("active_connection").GetString()
                .Should().Be("default");

            SqlConnectionManager.GetActiveConnectionName().Should().Be("default");
        }

        [Fact]
        public void SwitchConnection_RegisterAndSwitch_Roundtrip()
        {
            var name    = $"test-switch-tool-{Guid.NewGuid():N}";
            var connStr = "Server=fake;Database=fake;User Id=sa;Password=fake;TrustServerCertificate=true;";
            var prior   = SqlConnectionManager.GetActiveConnectionName();

            SqlConnectionManager.AddConnection(name, connStr, testConnection: false);

            var json = ConnectionManagement.SwitchConnection(name);
            var doc  = JsonDocument.Parse(json);

            doc.RootElement.TryGetProperty("error", out _).Should().BeFalse();
            doc.RootElement.GetProperty("data").GetProperty("active_connection").GetString()
                .Should().Be(name);

            // Restore
            SqlConnectionManager.SwitchConnection(prior);
            SqlConnectionManager.RemoveConnection(name);
        }

        // ── Response shape ────────────────────────────────────────────────────

        [Fact]
        public void GetConnections_ResponseHasExpectedTopLevelFields()
        {
            var json     = ConnectionManagement.GetConnections();
            var doc      = JsonDocument.Parse(json);
            var root     = doc.RootElement;

            root.TryGetProperty("server_name",      out _).Should().BeTrue();
            root.TryGetProperty("database",          out _).Should().BeTrue();
            root.TryGetProperty("connection_name",   out _).Should().BeTrue();
            root.TryGetProperty("operation",         out _).Should().BeTrue();
            root.TryGetProperty("timestamp",         out _).Should().BeTrue();
            root.TryGetProperty("execution_time_ms", out _).Should().BeTrue();
            root.TryGetProperty("security_mode",     out _).Should().BeTrue();
            root.TryGetProperty("data",              out _).Should().BeTrue();
        }
    }
}
