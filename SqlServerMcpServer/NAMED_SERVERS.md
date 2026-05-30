# Named MCP Server Configuration

This guide shows how to configure multiple named MCP server instances for different environments.

## Configuration Example

Your `mcp_config.json` can contain multiple named server instances:

```json
{
  "mcpServers": {
    "sqlserver-dev": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "D:\\GitHub\\cshap-mcp-sqlserver\\SqlServerMcpServer\\SqlServerMcpServer.csproj"
      ],
      "env": {
        "SQLSERVER_CONNECTION_STRING": "Server=localhost;Database=your_database;User Id=your_username;Password=your_password;TrustServerCertificate=true;",
        "MCP_ENVIRONMENT": "development",
        "MCP_SERVER_NAME": "Development SQL Server"
      }
    },
    "sqlserver-prod": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "D:\\GitHub\\cshap-mcp-sqlserver\\SqlServerMcpServer\\SqlServerMcpServer.csproj"
      ],
      "env": {
        "SQLSERVER_CONNECTION_STRING": "Server=prod-server;Database=production_db;User Id=prod_user;Password=prod_password;TrustServerCertificate=true;",
        "MCP_ENVIRONMENT": "production",
        "MCP_SERVER_NAME": "Production SQL Server"
      }
    },
    "sqlserver-staging": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "D:\\GitHub\\cshap-mcp-sqlserver\\SqlServerMcpServer\\SqlServerMcpServer.csproj"
      ],
      "env": {
        "SQLSERVER_CONNECTION_STRING": "Server=staging-server;Database=staging_db;User Id=staging_user;Password=staging_password;TrustServerCertificate=true;",
        "MCP_ENVIRONMENT": "staging",
        "MCP_SERVER_NAME": "Staging SQL Server"
      }
    }
  }
}
```

## Environment Variables

### Required Variables
- **`SQLSERVER_CONNECTION_STRING`**: Database connection string

### Optional Variables
- **`MCP_SERVER_NAME`**: Display name for the server (shown in responses)
- **`MCP_ENVIRONMENT`**: Environment identifier (dev, staging, prod, etc.)

## Usage in Windsurf

When you restart Windsurf, you'll see multiple SQL Server tools available:

1. **sqlserver-dev** - Development environment tools
2. **sqlserver-prod** - Production environment tools  
3. **sqlserver-staging** - Staging environment tools

Each server instance will:
- Connect to its respective database
- Show its name and environment in responses
- Maintain separate database contexts
- Apply the same query-validation security independently

## Example Response

```json
{
  "server_name": "Production SQL Server",
  "environment": "production",
  "current_database": "production_db",
  "connection_info": "Connected and ready",
  "security_mode": "GUARDED_WRITE",
  "allowed_operations": [
    "SELECT queries",
    "INSERT and UPDATE statements",
    "DELETE and TRUNCATE (require confirm_unsafe_operation=true)",
    "Database listing and switching",
    "Table schema inspection"
  ],
  "blocked_operations": [
    "DDL (DROP, CREATE, ALTER)",
    "MERGE, EXEC/EXECUTE, BULK",
    "GRANT, REVOKE, DENY",
    "SELECT INTO",
    "Multiple statements in one request"
  ]
}
```

## Benefits

1. **Environment Isolation**: Separate connections for dev/staging/prod
2. **Clear Context**: Always know which environment you're working with
3. **Security**: Each environment applies the same query-validation rules independently
4. **Flexibility**: Easy to add/remove environments as needed
5. **Audit Trail**: Server name and environment in all responses

## Adding New Environments

1. Add a new entry to `mcp_config.json` with a unique name
2. Set appropriate connection string and environment variables
3. Restart Windsurf to load the new server instance

Example for a testing environment:
```json
"sqlserver-test": {
  "command": "dotnet",
  "args": ["run", "--project", "D:\\GitHub\\cshap-mcp-sqlserver\\SqlServerMcpServer\\SqlServerMcpServer.csproj"],
  "env": {
    "SQLSERVER_CONNECTION_STRING": "Server=test-server;Database=test_db;User Id=test_user;Password=test_password;TrustServerCertificate=true;",
    "MCP_ENVIRONMENT": "testing",
    "MCP_SERVER_NAME": "Testing SQL Server"
  }
}
```
