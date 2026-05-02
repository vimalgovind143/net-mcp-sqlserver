# Multi-Instance SQL Server Support Implementation Plan

## Overview
Enable the SQL Server MCP Server to connect to multiple named SQL Server instances simultaneously while maintaining full backward compatibility.

## Design Principles

- **Lean first pass**: Deliver 90% of value with minimal complexity; expand later if needed
- **Minimal tool surface**: Only 3 new tools — avoid polluting the AI tool interface
- **Targeted `connectionName`**: Only on query execution tools, not all 35+ tools
- **Thread-safe**: Use `ConcurrentDictionary` for connection registry
- **Simple config**: Individual env vars per connection, not JSON blobs
- **Backward compatible**: Existing single-connection usage works unchanged

## Current State
- Single static connection managed by `SqlConnectionManager`
- All operations use the same implicit connection
- No support for named instances or hot-swapping

## Target State
- Named connection registry supporting multiple SQL instances
- `SwitchConnection` as primary mechanism for changing active connection
- Optional `connectionName` parameter only on query execution tools
- Per-connection resilience pipelines
- Connectivity tested on `AddConnection` (no separate health tool)

---

## Phase 1: Core Infrastructure

### 1.1 Create ConnectionInfo Model
**File:** `Configuration/ConnectionInfo.cs` (NEW)

```csharp
public class ConnectionInfo
{
    public string Name { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string CurrentDatabase { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsed { get; set; }
    public bool IsActive { get; set; }
}
```

### 1.2 Refactor SqlConnectionManager
**File:** `Configuration/SqlConnectionManager.cs` (REFACTOR)

**Changes:**
- Replace static fields with `ConcurrentDictionary<string, ConnectionInfo> _connections`
- Add `_activeConnectionName` field (volatile for thread safety)
- Auto-create "default" connection from env var on startup
- Load additional connections from individual env vars

**New Methods:**
```csharp
// Connection management
public static void AddConnection(string name, string connectionString, bool testFirst = true)
public static bool RemoveConnection(string name)
public static IReadOnlyCollection<string> GetConnectionNames()
public static bool ConnectionExists(string name)

// Connection switching
public static void SwitchConnection(string name)
public static string GetActiveConnectionName()
public static ConnectionInfo GetActiveConnection()

// Connection retrieval (connectionName only used by query tools)
public static ConnectionInfo GetConnection(string? name = null)
public static SqlConnection CreateConnection(string? name = null)
public static string GetCurrentConnectionString(string? name = null)
```

### 1.3 Update ConnectionPoolManager
**File:** `Configuration/ConnectionPoolManager.cs` (UPDATE)

**Changes:**
- Change from single pipeline to `ConcurrentDictionary<string, ResiliencePipeline>`
- Key by connection name
- Each connection gets isolated retry/circuit-breaker policy

---

## Phase 2: Configuration Loading

### 2.1 Individual Environment Variables (Primary)

Each named connection uses its own env var:

```
SQLSERVER_CONNECTION_STRING=Server=localhost;Database=master;...        → "default"
SQLSERVER_CONN_PROD=Server=prod-sql-01;Database=production;...         → "prod"
SQLSERVER_CONN_REPORTING=Server=report-sql-01;Database=analytics;...   → "reporting"
SQLSERVER_CONN_STAGING=Server=staging-sql;Database=staging;...         → "staging"
```

**Pattern:** `SQLSERVER_CONN_<NAME>` → connection name is the suffix, lowercased.

**In SqlConnectionManager static constructor:**
```csharp
static SqlConnectionManager()
{
    _connections = new ConcurrentDictionary<string, ConnectionInfo>(StringComparer.OrdinalIgnoreCase);

    // Auto-create default connection from existing env var
    var defaultConnectionString =
        Environment.GetEnvironmentVariable("SQLSERVER_CONNECTION_STRING")
        ?? GetConfigValue("SqlServer", "ConnectionString")
        ?? "Server=localhost,14333;Database=master;User Id=sa;Password=Your_Str0ng_Pass!;TrustServerCertificate=true;";

    _connections["default"] = new ConnectionInfo
    {
        Name = "default",
        ConnectionString = defaultConnectionString,
        ServerName = GetServerFromConnectionString(defaultConnectionString),
        CurrentDatabase = GetDatabaseFromConnectionString(defaultConnectionString),
        CreatedAt = DateTime.UtcNow,
        IsActive = true
    };
    _activeConnectionName = "default";

    // Scan for SQLSERVER_CONN_* env vars
    foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
    {
        var key = entry.Key?.ToString() ?? "";
        if (key.StartsWith("SQLSERVER_CONN_", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(entry.Value?.ToString()))
        {
            var name = key["SQLSERVER_CONN_".Length..].ToLowerInvariant();
            var connStr = entry.Value!.ToString()!;
            _connections[name] = new ConnectionInfo
            {
                Name = name,
                ConnectionString = connStr,
                ServerName = GetServerFromConnectionString(connStr),
                CurrentDatabase = GetDatabaseFromConnectionString(connStr),
                CreatedAt = DateTime.UtcNow,
                IsActive = false
            };
        }
    }
}
```

### 2.2 Appsettings.json (Optional, Phase 2 stretch)

```json
{
  "SqlServer": {
    "ConnectionString": "Server=localhost;Database=master;...",
    "NamedConnections": {
      "prod": "Server=prod-sql-01;Database=production;...",
      "reporting": "Server=report-sql-01;Database=analytics;..."
    }
  }
}
```

Loaded after env vars; env vars take precedence if same name exists.

---

## Phase 3: New Connection Management Tools (3 tools)

**File:** `SqlServerMcpServer.cs` + new `Operations/ConnectionManagement.cs`

### 3.1 GetConnections
```csharp
[McpServerTool, Description("List all configured SQL Server connections with their status")]
public static string GetConnections()
```
Returns:
```json
{
  "connections": [
    { "name": "default", "server": "localhost", "database": "master", "is_active": true },
    { "name": "prod", "server": "prod-sql-01", "database": "production", "is_active": false }
  ],
  "active_connection": "default",
  "total_connections": 2
}
```

### 3.2 AddConnection
```csharp
[McpServerTool, Description("Add a new named SQL Server connection. Tests connectivity before adding.")]
public static async Task<string> AddConnectionAsync(
    [Description("Unique name for this connection (e.g., 'prod', 'reporting')")] string name,
    [Description("SQL Server connection string")] string connectionString,
    [Description("Set as the active connection immediately (default: false)")] bool? setAsActive = false)
```

- Always tests connectivity before adding (no separate TestConnection tool)
- Returns success with server info, or error with troubleshooting steps
- Rejects if name "default" is used (reserved)

### 3.3 SwitchConnection
```csharp
[McpServerTool, Description("Switch the active connection to a different named SQL Server instance")]
public static string SwitchConnection(
    [Description("Name of the connection to activate")] string name)
```

- Returns connection info for the newly active connection
- Error if name doesn't exist (with list of available names)

**Removed from original plan:**
- `RemoveConnection` — rarely needed, can add later
- `TestConnection` — folded into `AddConnection`
- `GetConnectionHealth` — use existing `GetServerHealth` against active connection

---

## Phase 4: Add `connectionName` to Query Execution Tools Only

Only these tools get the optional `connectionName` parameter — the rest use the active connection via `SwitchConnection`:

### Tools to update:

**QueryExecution (primary use case for cross-instance queries):**
- `ExecuteQueryAsync` — add `connectionName` parameter
- `ReadQueryAsync` — add `connectionName` parameter

**SchemaInspection (useful for quick cross-instance comparison):**
- `GetTablesAsync` — add `connectionName` parameter

**That's it.** For all other tools (schema analysis, data discovery, performance, diagnostics, code generation), users switch the active connection first. This keeps the tool interface clean.

### Signature Pattern:
```csharp
public static async Task<string> ExecuteQueryAsync(
    [Description("SQL SELECT query to execute")] string query,
    [Description("Maximum rows to return (default: 100)")] int? maxRows = null,
    [Description("Connection name to use (defaults to active connection)")] string? connectionName = null)
```

### Operation Class Pattern:
```csharp
public static async Task<string> ExecuteQueryImplAsync(string query, int? maxRows, string? connectionName = null)
{
    using var connection = SqlConnectionManager.CreateConnection(connectionName);
    await connection.OpenAsync();
    // ... rest unchanged
}
```

---

## Phase 5: Response Context Updates

Update `ResponseFormatter` to include connection context when multiple connections exist:

```json
{
  "server_name": "SQL Server MCP",
  "database": "production",
  "connection_name": "prod",
  "security_mode": "READ_ONLY_ENFORCED",
  "data": { ... }
}
```

- `connection_name` field added only when more than one connection is registered
- Keeps responses clean for single-connection users

---

## Phase 6: Backward Compatibility Verification

### Requirements
- [ ] All existing tool calls work without changes
- [ ] No breaking changes to response format (new fields are additive)
- [ ] `SQLSERVER_CONNECTION_STRING` env var continues to work as "default"
- [ ] Single-connection users see no difference

### Implementation
- `CreateConnection(null)` resolves to active connection → "default" → same as before
- No existing tool signatures change (except 3 tools gaining an optional param)
- Response format only adds `connection_name` when >1 connection exists

---

## Phase 7: Testing

### Unit Tests to Add
- [ ] `SqlConnectionManagerTests`: AddConnection, SwitchConnection, env var parsing
- [ ] `ConnectionManagement tool tests`: GetConnections, AddConnection, SwitchConnection
- [ ] `BackwardCompatibilityTests`: Existing behavior unchanged with single connection

### Test Commands
```bash
dotnet build
dotnet test
```

---

## Phase 8: Verification Checklist

### Build & Test
- [ ] `dotnet build` succeeds with no errors
- [ ] `dotnet test` passes all existing tests
- [ ] All new tests pass

### Functionality
- [ ] Can add multiple named connections via env vars
- [ ] Can add connections at runtime via `AddConnection`
- [ ] Can switch between connections
- [ ] Can query specific connection via `connectionName` on ExecuteQuery
- [ ] Backward compatible with single connection usage
- [ ] Connection pool isolation per named connection

### Documentation
- [ ] AGENTS.md updated with multi-instance patterns
- [ ] Tool descriptions updated in code

---

## Files to Create
- `Configuration/ConnectionInfo.cs`
- `Operations/ConnectionManagement.cs`

## Files to Modify
- `Configuration/SqlConnectionManager.cs` (major refactor)
- `Configuration/ConnectionPoolManager.cs`
- `SqlServerMcpServer.cs` (add 3 tools, update 3 signatures)
- `Operations/QueryExecution.cs` (add connectionName param)
- `Operations/SchemaInspection.cs` (add connectionName to GetTables only)
- `Utilities/ResponseFormatter.cs` (add connection_name field)

## Files NOT Modified (unlike original plan)
- `Operations/DatabaseOperations.cs` — uses active connection
- `Operations/SchemaAnalysis.cs` — uses active connection
- `Operations/DataDiscovery.cs` — uses active connection
- `Operations/PerformanceAnalysis.cs` — uses active connection
- `Operations/Diagnostics.cs` — uses active connection
- `Operations/CodeGeneration.cs` — uses active connection

---

## Example Usage

```
User: "List my connections"
→ GetConnections()

User: "Add our prod server: Server=prod-sql-01;Database=production;..."
→ AddConnection(name: "prod", connectionString: "...", setAsActive: true)

User: "Show me all tables"
→ GetTables()  // uses active "prod" connection

User: "Switch to the default connection"
→ SwitchConnection(name: "default")

User: "Run this query against prod: SELECT TOP 10 * FROM Orders"
→ ExecuteQuery(query: "...", connectionName: "prod")  // targets prod without switching
```

---

## Implementation Order

1. **Phase 1.1** — Create ConnectionInfo model
2. **Phase 1.2** — Refactor SqlConnectionManager with ConcurrentDictionary
3. **Phase 1.3** — Update ConnectionPoolManager
4. **Phase 2** — Env var scanning (SQLSERVER_CONN_*)
5. **Phase 3** — Add 3 new tools (GetConnections, AddConnection, SwitchConnection)
6. **Phase 4** — Add connectionName to ExecuteQuery, ReadQuery, GetTables
7. **Phase 5** — Response context updates
8. **Phase 6** — Backward compatibility verification
9. **Phase 7** — Tests
10. **Phase 8** — Final verification

---

**Estimated Effort:** 2-3 hours
**Complexity:** Medium (focused changes, fewer files touched)
**Risk:** Low-Medium (backward compatibility maintained, minimal surface area)
