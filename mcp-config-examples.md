# MCP Client Configuration Examples

Sample configuration files for connecting this SQL Server MCP Server to various agentic AI IDEs.

> **Single-instance users:** Nothing changes — the examples below are fully backward compatible.  
> **Multi-instance users:** Add `SQLSERVER_CONN_<NAME>` env vars for each additional server.
> **Docker deployment:** See `config-samples/` directory for ready-to-use config files.

---

## VS Code (`.vscode/mcp.json`)

### Single instance (default)
```json
{
  "servers": {
    "sqlserver": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "SqlServerMcpServer"],
      "env": {
        "SQLSERVER_CONNECTION_STRING": "Server=localhost,1433;Database=master;User Id=sa;Password=YourPass!;TrustServerCertificate=true;"
      }
    }
  }
}
```

### Multiple instances (pre-configured)
```json
{
  "servers": {
    "sqlserver": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "SqlServerMcpServer"],
      "env": {
        "SQLSERVER_CONNECTION_STRING":  "Server=localhost,1433;Database=master;User Id=sa;Password=LocalPass!;TrustServerCertificate=true;",
        "SQLSERVER_CONN_PROD":          "Server=prod-sql-01.company.com;Database=production;User Id=app_user;Password=ProdPass!;TrustServerCertificate=true;",
        "SQLSERVER_CONN_REPORTING":     "Server=report-sql-01.company.com;Database=analytics;User Id=report_user;Password=ReportPass!;TrustServerCertificate=true;",
        "SQLSERVER_CONN_STAGING":       "Server=staging-sql.company.com;Database=staging;User Id=sa;Password=StagingPass!;TrustServerCertificate=true;"
      }
    }
  }
}
```

### Using the published executable (recommended for production)
```json
{
  "servers": {
    "sqlserver": {
      "type": "stdio",
      "command": "/absolute/path/to/publish/SqlServerMcpServer",
      "args": [],
      "env": {
        "SQLSERVER_CONNECTION_STRING":  "Server=localhost,1433;Database=master;User Id=sa;Password=YourPass!;TrustServerCertificate=true;",
        "SQLSERVER_CONN_PROD":          "Server=prod-sql-01.company.com;Database=production;User Id=app_user;Password=ProdPass!;TrustServerCertificate=true;"
      }
    }
  }
}
```

---

## Claude Desktop (`claude_desktop_config.json`)

### Single instance
```json
{
  "mcpServers": {
    "sqlserver": {
      "command": "/absolute/path/to/publish/SqlServerMcpServer",
      "args": [],
      "env": {
        "SQLSERVER_CONNECTION_STRING": "Server=localhost,1433;Database=master;User Id=sa;Password=YourPass!;TrustServerCertificate=true;"
      }
    }
  }
}
```

### Multiple instances
```json
{
  "mcpServers": {
    "sqlserver": {
      "command": "/absolute/path/to/publish/SqlServerMcpServer",
      "args": [],
      "env": {
        "SQLSERVER_CONNECTION_STRING":  "Server=localhost,1433;Database=master;User Id=sa;Password=LocalPass!;TrustServerCertificate=true;",
        "SQLSERVER_CONN_PROD":          "Server=prod-sql-01.company.com;Database=production;User Id=app_user;Password=ProdPass!;TrustServerCertificate=true;",
        "SQLSERVER_CONN_REPORTING":     "Server=report-sql-01.company.com;Database=analytics;User Id=report_user;Password=ReportPass!;TrustServerCertificate=true;"
      }
    }
  }
}
```

**Windows path example:**
```json
{
  "mcpServers": {
    "sqlserver": {
      "command": "C:\\path\\to\\publish\\SqlServerMcpServer.exe",
      "args": [],
      "env": {
        "SQLSERVER_CONNECTION_STRING": "Server=localhost,1433;Database=master;User Id=sa;Password=YourPass!;TrustServerCertificate=true;"
      }
    }
  }
}
```

---

## Cursor / Windsurf / Cline (`cline_mcp_settings.json` or `mcp_settings.json`)

```json
{
  "mcpServers": {
    "sqlserver": {
      "command": "dotnet",
      "args": ["run", "--project", "/absolute/path/to/SqlServerMcpServer"],
      "env": {
        "SQLSERVER_CONNECTION_STRING":  "Server=localhost,1433;Database=master;User Id=sa;Password=LocalPass!;TrustServerCertificate=true;",
        "SQLSERVER_CONN_PROD":          "Server=prod-sql-01.company.com;Database=production;User Id=app_user;Password=ProdPass!;TrustServerCertificate=true;"
      },
      "disabled": false,
      "autoApprove": [
        "GetConnections",
        "SwitchConnection",
        "GetServerHealth",
        "GetCurrentDatabase",
        "GetDatabases",
        "GetTables",
        "GetTableSchema",
        "GetStoredProcedures"
      ]
    }
  }
}
```

---

## Kilo / OpenCode (`.kilo/mcp.json` or equivalent)

```json
{
  "mcpServers": {
    "sqlserver": {
      "command": "dotnet",
      "args": ["run", "--project", "SqlServerMcpServer"],
      "env": {
        "SQLSERVER_CONNECTION_STRING":  "Server=localhost,1433;Database=master;User Id=sa;Password=LocalPass!;TrustServerCertificate=true;",
        "SQLSERVER_CONN_PROD":          "Server=prod-sql-01.company.com;Database=production;User Id=app_user;Password=ProdPass!;TrustServerCertificate=true;"
      }
    }
  }
}
```

---

## Environment Variable Reference

| Variable | Description | Example |
|---|---|---|
| `SQLSERVER_CONNECTION_STRING` | Default connection (always "default") | `Server=localhost,1433;Database=master;...` |
| `SQLSERVER_CONN_<NAME>` | Named connection (name = suffix, lowercased) | `SQLSERVER_CONN_PROD` → `"prod"` |
| `SQLSERVER_COMMAND_TIMEOUT` | Query timeout in seconds (default: 30) | `60` |
| `MCP_SERVER_NAME` | Display name in responses | `"My SQL MCP"` |
| `MCP_ENVIRONMENT` | Environment label in responses | `"production"` |
| `MCP_TRANSPORT` | Transport mode: `stdio`, `tcp`, `http`, `sse` | `"sse"` |
| `MCP_PORT` | Port for TCP/SSE transport (default: 8080) | `8080` |

---

## Available Multi-Instance Tools

After connecting, these tools manage named connections:

| Tool | Description |
|---|---|
| `GetConnections` | List all registered connections and which is active |
| `AddConnection` | Register a new named connection (tests connectivity first) |
| `SwitchConnection` | Change the active connection for all subsequent calls |

### Cross-connection queries (no switch needed)

These tools accept an optional `connectionName` parameter to target a specific connection
without changing the active connection:

| Tool | `connectionName` parameter |
|---|---|
| `ExecuteQuery` | ✅ |
| `ReadQuery` | ✅ |
| `GetTables` | ✅ |

---

## Quick Start Conversation

```
You:       "List my SQL connections"
Assistant: [calls GetConnections]
           → default (localhost, active), prod, reporting

You:       "Switch to prod and show all tables"
Assistant: [calls SwitchConnection("prod")]
           [calls GetTables()]

You:       "Run SELECT TOP 5 * FROM Orders against prod while I work on default"
Assistant: [calls SwitchConnection("default")]
           [calls ExecuteQuery("SELECT TOP 5 * FROM Orders", connectionName: "prod")]

---

## Transport Modes

The server supports three transport modes, selected via `MCP_TRANSPORT`:

| Mode | Value | Description |
|------|-------|-------------|
| **Stdio** | `stdio` (default) | Stdin/stdout — standard local process transport |
| **TCP** | `tcp` | Raw TCP socket on `MCP_PORT` (one client at a time) |
| **SSE** | `sse` or `http` | HTTP SSE transport on `MCP_PORT` — endpoints: `GET /sse`, `POST /message`, `GET /health` |

---

## Docker Deployment

### Build

```bash
docker build -t sqlserver-mcp .
```

### Run

```bash
docker run -d \
  --name sqlserver-mcp \
  -p 8080:8080 \
  -e SQLSERVER_CONNECTION_STRING="Server=host.docker.internal,1433;Database=master;User Id=sa;Password=YourPass!;TrustServerCertificate=true;" \
  -e MCP_TRANSPORT=sse \
  sqlserver-mcp
```

### Docker Compose

Create a `.env` file:

```env
SQLSERVER_CONNECTION_STRING=Server=host.docker.internal,1433;Database=master;User Id=sa;Password=YourPass!;TrustServerCertificate=true;
MCP_TRANSPORT=sse
MCP_PORT=8080
```

Then run:

```bash
docker compose up -d
```

Check health:

```bash
curl http://localhost:8080/health
# → {"status":"healthy","transport":"sse"}
```

---

## SSE Transport — Client Configuration

The SSE transport supports **multiple concurrent sessions** via session IDs.

### How it works

1. Client connects `GET /sse` → receives `event: endpoint` with session-specific URL  
2. Client POSTs JSON-RPC to `/message?sessionId=<id>`  
3. Server streams responses as SSE events on the open `/sse` connection

### VS Code (`.vscode/mcp.json`) — SSE transport

```json
{
  "servers": {
    "sqlserver": {
      "type": "sse",
      "url": "http://localhost:9090/sse"
    }
  }
}
```

### Kilo (`.kilo/kilo.jsonc`) — SSE (remote) transport

```jsonc
{
  "mcp": {
    "sqlserver": {
      "type": "remote",
      "url": "http://localhost:9090/sse",
      "enabled": true,
      "timeout": 60000
    }
  },
  "permission": {
    "sqlserver_*": "allow"
  }
}
```

### Cursor / Windsurf — SSE transport

```json
{
  "mcpServers": {
    "sqlserver": {
      "type": "sse",
      "url": "http://localhost:9090/sse"
    }
  }
}
```

> **Note:** Claude Desktop only supports `stdio` transport at this time.

### Docker via Stdio (Claude Desktop / any stdio client)

```json
{
  "mcpServers": {
    "sqlserver": {
      "command": "docker",
      "args": [
        "run",
        "-i",
        "--rm",
        "-e", "SQLSERVER_CONNECTION_STRING=Server=host.docker.internal,1433;Database=master;User Id=sa;Password=YourPass!;TrustServerCertificate=true;",
        "-e", "MCP_TRANSPORT=stdio",
        "sqlserver-mcp"
      ]
    }
  }
}
```

---

## TCP Transport — Client Configuration

For clients that support raw TCP (or via a TCP→stdio proxy):

```bash
# Run the server in TCP mode
docker run -d \
  --name sqlserver-mcp \
  -p 9090:8080 \
  -e SQLSERVER_CONNECTION_STRING="Server=host.docker.internal,1433;Database=master;User Id=sa;Password=YourPass!;TrustServerCertificate=true;" \
  -e MCP_TRANSPORT=tcp \
  -e MCP_PORT=8080 \
  sqlserver-mcp
```

Then connect via `localhost:9090`.

---

## Building for a Specific Platform

```bash
# Linux x64 (default for Docker)
dotnet publish -c Release -r linux-x64 -o publish/

# Linux ARM64
dotnet publish -c Release -r linux-arm64 -o publish/

# Windows x64
dotnet publish -c Release -r win-x64 -o publish/
```
```
