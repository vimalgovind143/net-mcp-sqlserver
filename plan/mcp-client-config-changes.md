# MCP Client Configuration Changes for Multi-Instance Support

## Overview
How MCP clients (VS Code, Claude Desktop, Cline, etc.) configure multiple SQL Server connections.

## Current Configuration (Single Instance — Unchanged)

### VS Code (.vscode/mcp.json)
```json
{
  "servers": {
    "sqlserver": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "SqlServerMcpServer"],
      "env": {
        "SQLSERVER_CONNECTION_STRING": "Server=localhost;Database=master;User Id=sa;Password=...;"
      }
    }
  }
}
```

### Claude Desktop (claude_desktop_config.json)
```json
{
  "mcpServers": {
    "sqlserver": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/SqlServerMcpServer"],
      "env": {
        "SQLSERVER_CONNECTION_STRING": "Server=localhost;Database=master;User Id=sa;Password=...;"
      }
    }
  }
}
```

---

## Multi-Instance Configuration

### Option 1: Individual Environment Variables (Recommended)

Each connection gets its own env var using the pattern `SQLSERVER_CONN_<NAME>`. The name suffix becomes the connection name (lowercased).

#### VS Code (.vscode/mcp.json)
```json
{
  "servers": {
    "sqlserver": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "SqlServerMcpServer"],
      "env": {
        "SQLSERVER_CONNECTION_STRING": "Server=localhost;Database=master;User Id=sa;Password=...;",
        "SQLSERVER_CONN_PROD": "Server=prod-sql-01.company.com;Database=production;User Id=app_user;Password=...;",
        "SQLSERVER_CONN_REPORTING": "Server=report-sql-01.company.com;Database=analytics;User Id=report_user;Password=...;",
        "SQLSERVER_CONN_STAGING": "Server=staging-sql;Database=staging;User Id=sa;Password=...;"
      }
    }
  }
}
```

#### Claude Desktop (claude_desktop_config.json)
```json
{
  "mcpServers": {
    "sqlserver": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/SqlServerMcpServer"],
      "env": {
        "SQLSERVER_CONNECTION_STRING": "Server=localhost;Database=master;User Id=sa;Password=...;",
        "SQLSERVER_CONN_PROD": "Server=prod-sql-01;Database=production;User Id=app_user;Password=...;",
        "SQLSERVER_CONN_REPORTING": "Server=report-sql-01;Database=analytics;User Id=report_user;Password=...;"
      }
    }
  }
}
```

#### Cline (cline_mcp_settings.json)
```json
{
  "mcpServers": {
    "sqlserver": {
      "command": "dotnet",
      "args": ["run", "--project", "SqlServerMcpServer"],
      "env": {
        "SQLSERVER_CONNECTION_STRING": "Server=localhost;Database=master;User Id=sa;Password=...;",
        "SQLSERVER_CONN_PROD": "Server=prod-sql-01;Database=production;..."
      },
      "disabled": false,
      "autoApprove": ["GetConnections", "GetServerHealth"]
    }
  }
}
```

**Advantages over JSON blob env var:**
- Each connection string is a separate, readable env var
- Easy to add/remove connections
- Compatible with secret managers that inject individual env vars
- No JSON escaping issues

---

### Option 2: Runtime Connection Management (No Config Changes)

**No changes required to MCP config files.**

Connections are added dynamically at runtime using the `AddConnection` tool:

```
User: Add a connection named "prod" to Server=prod-sql-01;Database=production;...
Assistant: [Calls AddConnection tool — tests connectivity, then registers]

User: Query the tables in prod
Assistant: [Calls GetTables with connectionName="prod"]
```

Best for: development, ad-hoc exploration, temporary connections.

---

### Option 3: Appsettings.json (Optional)

Add connections to `appsettings.json` in the project root:

```json
{
  "SqlServer": {
    "ConnectionString": "Server=localhost;Database=master;...",
    "CommandTimeout": 30,
    "NamedConnections": {
      "prod": "Server=prod-sql-01.company.com;Database=production;...",
      "reporting": "Server=report-sql-01.company.com;Database=analytics;..."
    }
  }
}
```

MCP client config stays minimal (just the default connection or nothing).

**Precedence:** Environment variables > appsettings.json > built-in defaults.

---

## Client-Side Usage Examples

### Pre-configured Connections (Option 1)

```
User: "List all my SQL Server connections"
→ GetConnections
Response: default (active), prod, reporting, staging

User: "Show me tables in prod"
→ GetTables(connectionName: "prod")   // direct targeting, no switch needed

User: "Switch to reporting and show me its tables"
→ SwitchConnection(name: "reporting")
→ GetTables()                          // uses active connection
```

### Runtime Management (Option 2)

```
User: "Connect to our prod server at prod-sql-01.company.com"
→ AddConnection(name: "prod", connectionString: "Server=prod-sql-01...", setAsActive: true)

User: "Show me all tables"
→ GetTables()                          // uses active "prod" connection

User: "Run SELECT TOP 10 * FROM Orders against prod while I'm on default"
→ SwitchConnection(name: "default")
→ ExecuteQuery(query: "SELECT TOP 10 * FROM Orders", connectionName: "prod")
```

---

## Summary

| Approach | Config Changes | Best For |
|----------|---------------|----------|
| **Individual env vars** (`SQLSERVER_CONN_*`) | Add env vars per connection | Production, stable environments |
| **Runtime management** | None | Development, ad-hoc exploration |
| **Appsettings.json** | Edit appsettings.json | File-based config, version controlled |

## Recommendation

- **Development/testing:** Use runtime management (Option 2) — zero config
- **Production/stable:** Use individual env vars (Option 1) — simple, secure, works with secret managers
- **Team/shared:** Use appsettings.json (Option 3) — version controllable
