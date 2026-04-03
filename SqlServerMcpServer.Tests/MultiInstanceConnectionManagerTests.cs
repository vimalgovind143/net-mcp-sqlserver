using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using SqlServerMcpServer.Configuration;
using Xunit;

namespace SqlServerMcpServer.Tests
{
    /// <summary>
    /// Unit tests for the multi-instance SqlConnectionManager functionality.
    /// Tests that do NOT require a live database connection focus on in-memory
    /// registry behaviour and configuration parsing.
    /// Tests that DO require a live connection are marked with [Trait("Category", "Integration")].
    /// </summary>
    public class MultiInstanceConnectionManagerTests
    {
        // ── registry basics ─────────────────────────────────────────────────

        [Fact]
        public void DefaultConnection_IsAlwaysRegistered()
        {
            var names = SqlConnectionManager.GetConnectionNames();
            names.Should().Contain("default", because: "a 'default' connection is bootstrapped at startup");
        }

        [Fact]
        public void GetActiveConnectionName_ReturnsNonEmptyString()
        {
            var active = SqlConnectionManager.GetActiveConnectionName();
            active.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public void GetAllConnections_ContainsAtLeastOneEntry()
        {
            var all = SqlConnectionManager.GetAllConnections();
            all.Should().NotBeEmpty();
        }

        [Fact]
        public void ConnectionExists_ReturnsTrueForDefault()
        {
            SqlConnectionManager.ConnectionExists("default").Should().BeTrue();
        }

        [Fact]
        public void ConnectionExists_ReturnsFalseForNonExistentName()
        {
            SqlConnectionManager.ConnectionExists("__nonexistent_xyz__").Should().BeFalse();
        }

        [Fact]
        public void ConnectionExists_ReturnsFalseForNullOrEmpty()
        {
            SqlConnectionManager.ConnectionExists(null!).Should().BeFalse();
            SqlConnectionManager.ConnectionExists("").Should().BeFalse();
            SqlConnectionManager.ConnectionExists("   ").Should().BeFalse();
        }

        // ── AddConnection (no-test-connectivity path) ────────────────────────

        [Fact]
        public void AddConnection_WithTestConnectionFalse_RegistersEntry()
        {
            var uniqueName = $"test-add-{Guid.NewGuid():N}";
            var connStr = "Server=fake-host;Database=fake-db;User Id=sa;Password=fake;TrustServerCertificate=true;";

            SqlConnectionManager.AddConnection(uniqueName, connStr, testConnection: false);

            SqlConnectionManager.ConnectionExists(uniqueName).Should().BeTrue();
            SqlConnectionManager.GetConnectionNames().Should().Contain(uniqueName);

            // Clean up
            SqlConnectionManager.RemoveConnection(uniqueName);
        }

        [Fact]
        public void AddConnection_SetsAsActive_WhenRequested()
        {
            var uniqueName = $"test-active-{Guid.NewGuid():N}";
            var connStr = "Server=fake-host;Database=fake-db;User Id=sa;Password=fake;TrustServerCertificate=true;";
            var previousActive = SqlConnectionManager.GetActiveConnectionName();

            SqlConnectionManager.AddConnection(uniqueName, connStr, testConnection: false, setAsActive: true);

            SqlConnectionManager.GetActiveConnectionName().Should().Be(uniqueName);

            // Restore
            SqlConnectionManager.SwitchConnection(previousActive);
            SqlConnectionManager.RemoveConnection(uniqueName);
        }

        [Fact]
        public void AddConnection_ThrowsOnEmptyName()
        {
            var action = () => SqlConnectionManager.AddConnection("", "Server=x;", testConnection: false);
            action.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void AddConnection_ThrowsOnEmptyConnectionString()
        {
            var action = () => SqlConnectionManager.AddConnection("myconn", "", testConnection: false);
            action.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void AddConnection_OverwritesExistingEntry()
        {
            var name    = $"test-overwrite-{Guid.NewGuid():N}";
            var connStr1 = "Server=host1;Database=db1;User Id=sa;Password=p1;TrustServerCertificate=true;";
            var connStr2 = "Server=host2;Database=db2;User Id=sa;Password=p2;TrustServerCertificate=true;";

            SqlConnectionManager.AddConnection(name, connStr1, testConnection: false);
            SqlConnectionManager.AddConnection(name, connStr2, testConnection: false);

            var info = SqlConnectionManager.GetConnection(name);
            info.ServerName.Should().Be("host2");

            SqlConnectionManager.RemoveConnection(name);
        }

        // ── RemoveConnection ──────────────────────────────────────────────────

        [Fact]
        public void RemoveConnection_ReturnsFalseForDefault()
        {
            // "default" is protected
            SqlConnectionManager.RemoveConnection("default").Should().BeFalse();
            SqlConnectionManager.ConnectionExists("default").Should().BeTrue();
        }

        [Fact]
        public void RemoveConnection_ReturnsFalseForNonExistent()
        {
            SqlConnectionManager.RemoveConnection("__does_not_exist__").Should().BeFalse();
        }

        [Fact]
        public void RemoveConnection_RemovesRegisteredEntry()
        {
            var name    = $"test-remove-{Guid.NewGuid():N}";
            var connStr = "Server=fake;Database=fake;User Id=sa;Password=fake;TrustServerCertificate=true;";

            SqlConnectionManager.AddConnection(name, connStr, testConnection: false);
            SqlConnectionManager.ConnectionExists(name).Should().BeTrue();

            var result = SqlConnectionManager.RemoveConnection(name);
            result.Should().BeTrue();
            SqlConnectionManager.ConnectionExists(name).Should().BeFalse();
        }

        [Fact]
        public void RemoveConnection_WhenActiveFallsBackToDefault()
        {
            var name    = $"test-remove-active-{Guid.NewGuid():N}";
            var connStr = "Server=fake;Database=fake;User Id=sa;Password=fake;TrustServerCertificate=true;";

            SqlConnectionManager.AddConnection(name, connStr, testConnection: false, setAsActive: true);
            SqlConnectionManager.GetActiveConnectionName().Should().Be(name);

            SqlConnectionManager.RemoveConnection(name);

            SqlConnectionManager.GetActiveConnectionName().Should().Be("default");
        }

        // ── SwitchConnection ──────────────────────────────────────────────────

        [Fact]
        public void SwitchConnection_ChangesActiveConnection()
        {
            var name    = $"test-switch-{Guid.NewGuid():N}";
            var connStr = "Server=fake;Database=fake;User Id=sa;Password=fake;TrustServerCertificate=true;";
            var prior   = SqlConnectionManager.GetActiveConnectionName();

            SqlConnectionManager.AddConnection(name, connStr, testConnection: false);
            SqlConnectionManager.SwitchConnection(name);

            SqlConnectionManager.GetActiveConnectionName().Should().Be(name);

            // Restore
            SqlConnectionManager.SwitchConnection(prior);
            SqlConnectionManager.RemoveConnection(name);
        }

        [Fact]
        public void SwitchConnection_ThrowsForUnknownName()
        {
            var action = () => SqlConnectionManager.SwitchConnection("__nonexistent_xyz__");
            action.Should().Throw<KeyNotFoundException>();
        }

        [Fact]
        public void SwitchConnection_MarksOnlyTargetAsActive()
        {
            var name    = $"test-mark-{Guid.NewGuid():N}";
            var connStr = "Server=fake;Database=fake;User Id=sa;Password=fake;TrustServerCertificate=true;";
            var prior   = SqlConnectionManager.GetActiveConnectionName();

            SqlConnectionManager.AddConnection(name, connStr, testConnection: false);
            SqlConnectionManager.SwitchConnection(name);

            var all = SqlConnectionManager.GetAllConnections();
            all.Where(c => c.IsActive).Should().HaveCount(1);
            all.Single(c => c.IsActive).Name.Should().Be(name);

            // Restore
            SqlConnectionManager.SwitchConnection(prior);
            SqlConnectionManager.RemoveConnection(name);
        }

        // ── GetConnection ─────────────────────────────────────────────────────

        [Fact]
        public void GetConnection_WithNullReturnsActiveConnection()
        {
            var active     = SqlConnectionManager.GetActiveConnectionName();
            var activeInfo = SqlConnectionManager.GetConnection(null);
            activeInfo.Name.Should().Be(active);
        }

        [Fact]
        public void GetConnection_WithNameReturnsCorrectEntry()
        {
            var name    = $"test-get-{Guid.NewGuid():N}";
            var connStr = "Server=get-host;Database=get-db;User Id=sa;Password=fake;TrustServerCertificate=true;";

            SqlConnectionManager.AddConnection(name, connStr, testConnection: false);
            var info = SqlConnectionManager.GetConnection(name);

            info.Name.Should().Be(name);
            info.ServerName.Should().Be("get-host");
            info.CurrentDatabase.Should().Be("get-db");

            SqlConnectionManager.RemoveConnection(name);
        }

        [Fact]
        public void GetConnection_ThrowsForUnknownName()
        {
            var action = () => SqlConnectionManager.GetConnection("__nonexistent__");
            action.Should().Throw<KeyNotFoundException>();
        }

        // ── CreateConnection ──────────────────────────────────────────────────

        [Fact]
        public void CreateConnection_WithNullUsesActiveConnectionString()
        {
            var activeInfo = SqlConnectionManager.GetConnection();
            using var conn = SqlConnectionManager.CreateConnection(null);

            conn.Should().NotBeNull();
            conn.ConnectionString.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public void CreateConnection_WithNameUsesNamedConnectionString()
        {
            var name    = $"test-create-{Guid.NewGuid():N}";
            var connStr = "Server=create-host;Database=create-db;User Id=sa;Password=fake;TrustServerCertificate=true;";

            SqlConnectionManager.AddConnection(name, connStr, testConnection: false);
            using var conn = SqlConnectionManager.CreateConnection(name);

            conn.Should().NotBeNull();
            conn.ConnectionString.Should().Contain("create-host");

            SqlConnectionManager.RemoveConnection(name);
        }

        [Fact]
        public void CreateConnection_UpdatesLastUsed()
        {
            var name    = $"test-lastused-{Guid.NewGuid():N}";
            var connStr = "Server=lu-host;Database=lu-db;User Id=sa;Password=fake;TrustServerCertificate=true;";

            SqlConnectionManager.AddConnection(name, connStr, testConnection: false);
            var before = SqlConnectionManager.GetConnection(name).LastUsed;

            using var conn = SqlConnectionManager.CreateConnection(name);
            var after = SqlConnectionManager.GetConnection(name).LastUsed;

            after.Should().NotBeNull();
            after.Should().BeOnOrAfter(before ?? DateTime.MinValue);

            SqlConnectionManager.RemoveConnection(name);
        }

        // ── ConnectionInfo model ──────────────────────────────────────────────

        [Fact]
        public void ConnectionInfo_DefaultConnection_HasExpectedShape()
        {
            var info = SqlConnectionManager.GetConnection("default");

            info.Name.Should().Be("default");
            info.ConnectionString.Should().NotBeNullOrWhiteSpace();
            info.ServerName.Should().NotBeNullOrWhiteSpace();
            info.CurrentDatabase.Should().NotBeNullOrWhiteSpace();
            info.CreatedAt.Should().BeOnOrBefore(DateTime.UtcNow);
        }

        // ── CreateConnectionStringForDatabase ────────────────────────────────

        [Fact]
        public void CreateConnectionStringForDatabase_WithValidName_ContainsDatabase()
        {
            var result = SqlConnectionManager.CreateConnectionStringForDatabase("TestDB");
            result.Should().Contain("TestDB");
        }

        [Fact]
        public void CreateConnectionStringForDatabase_WithNull_ReturnsCurrentConnectionString()
        {
            var result = SqlConnectionManager.CreateConnectionStringForDatabase(null);
            result.Should().Be(SqlConnectionManager.CurrentConnectionString);
        }

        // ── Backward-compat static properties ───────────────────────────────

        [Fact]
        public void BackwardCompat_CurrentConnectionString_NotNull()
        {
            SqlConnectionManager.CurrentConnectionString.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public void BackwardCompat_CurrentDatabase_NotNull()
        {
            SqlConnectionManager.CurrentDatabase.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public void BackwardCompat_ServerName_NotNull()
        {
            SqlConnectionManager.ServerName.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public void BackwardCompat_CommandTimeout_Positive()
        {
            SqlConnectionManager.CommandTimeout.Should().BeGreaterThan(0);
        }

        [Fact]
        public void BackwardCompat_CreateConnection_NoArgs_UsesActiveConnection()
        {
            using var conn = SqlConnectionManager.CreateConnection();
            conn.Should().NotBeNull();
        }
    }
}
