# SQL Server MCP Server

A Model Context Protocol (MCP) server that provides tools for interacting with Microsoft SQL Server databases, built with the official C# SDK.

## Features

- **🔒 Read-Only Security**: Enforced SELECT-only operations to prevent accidental data modification
- **Dynamic Database Switching**: Switch between databases on the same server without restarting
- **Database Listing**: View all available databases with current database highlighted
- **Execute SQL Queries**: Run read-only SQL queries against the current database
- **List Tables**: Get all tables in the current database with row counts
- **Get Table Schema**: Retrieve column information for specific tables
- **Connection Info**: Display current database connection status
- **Stored Procedures**: List stored procedures with schema and dates, get detailed info including parameters, definitions, and dependencies
- **Object Definition**: Unified endpoint to get detailed information for any database object (procedures, functions, views) including definitions, parameters/columns, and dependencies
- **Batch Object Definitions**: Request multiple object definitions in one call with limits and per-object status
- **Health Check**: Verify connectivity and view server properties
- **Structured Logging**: JSON logs to stderr with correlation IDs and timings
 - **Serilog Integration**: Structured logging via Serilog with JSON formatting
 - **Row Limit Enforcement**: Max 100 rows returned per query

## 🔒 Security Features

This MCP server is designed with **read-only security** to prevent accidental data modification:

### **Blocked Operations:**
- ❌ INSERT, UPDATE, DELETE statements
- ❌ DROP, CREATE, ALTER statements  
- ❌ TRUNCATE, MERGE operations
- ❌ EXEC/EXECUTE stored procedures
- ❌ GRANT, REVOKE, DENY permissions
- ❌ BULK operations
- ❌ SELECT INTO (object creation)
- ❌ Multiple statements in a single request
- ❌ Any non-SELECT statements
 - ❌ USE, SET, DBCC, BACKUP, RESTORE, RECONFIGURE, sp_configure

### **Allowed Operations:**
- ✅ SELECT queries for data retrieval
- ✅ Database listing and switching
- ✅ Table schema inspection
- ✅ Connection status queries

### **Error Messages:**
When a blocked operation is attempted, the server provides clear, specific error messages:
```
❌ UPDATE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.
```

### **Additional Safety Features:**
- Query timeout protection (configurable, default 30 seconds)
- Input validation and sanitization
- Detailed error reporting with helpful guidance
- Security mode indicators in all responses

## Dynamic Database Management

The MCP server now supports dynamic database switching, allowing you to:

1. **List all databases** - See every database on the SQL Server instance
2. **Switch databases** - Change the active database without restarting the server
3. **Track current context** - All operations (queries, table listing) work on the current database
4. **Connection validation** - Server validates database connections before switching

**Example Workflow:**
```
User: List all databases
Server: [Shows all databases with current one highlighted]

User: Switch to Northwind database  
Server: [Successfully switches and confirms]

User: Show me all tables
Server: [Shows tables from Northwind database]
```

This feature is particularly useful when working with multiple databases on the same server, such as development, staging, and production environments.

## Setup

### Prerequisites

- .NET 10.0 SDK
- SQL Server (local or remote)
- Access to the target database

### Installation

1. Clone or download this project
2. Navigate to the project directory
3. Restore NuGet packages:

```bash
dotnet restore
```

4. Build the project:

```bash
dotnet build
```

### Configuration

Configuration values can be provided via environment variables or `appsettings.json`. Environment variables take precedence.

Set the connection string using an environment variable:

```bash
# Windows
set SQLSERVER_CONNECTION_STRING="Server=your_server;Database=your_database;User Id=your_username;Password=your_password;TrustServerCertificate=true;"

# PowerShell
$env:SQLSERVER_CONNECTION_STRING="Server=your_server;Database=your_database;User Id=your_username;Password=your_password;TrustServerCertificate=true;"

# Linux/macOS
export SQLSERVER_CONNECTION_STRING="Server=your_server;Database=your_database;User Id=your_username;Password=your_password;TrustServerCertificate=true;"
```

**Default Connection String**: If no environment variable is set, it defaults to:
```
Server=localhost;Database=master;Trusted_Connection=true;TrustServerCertificate=true;
```

You can also configure the SQL command timeout (in seconds):

```bash
# Windows
set SQLSERVER_COMMAND_TIMEOUT=60

# PowerShell
$env:SQLSERVER_COMMAND_TIMEOUT=60

# Linux/macOS
export SQLSERVER_COMMAND_TIMEOUT=60
```

If not set, the timeout defaults to `30` seconds.

Alternatively, you can configure via `appsettings.json` placed alongside the executable or under the project directory:

```
{
  "SqlServer": {
    "ConnectionString": "Server=localhost;Database=master;Trusted_Connection=true;TrustServerCertificate=true;",
    "CommandTimeout": 30
  }
}
```

Precedence: `Environment variables` → `appsettings.json` → built-in defaults.

### Running the Server

```bash
dotnet run
```

The server will start and listen for MCP protocol messages via stdio.

## Integration with Claude Desktop

1. Copy the `claude_desktop_config.json` file to your Claude Desktop configuration directory
2. Update the connection string in the config file to match your database
3. Restart Claude Desktop

The configuration file should be placed at:
- **Windows**: `%APPDATA%\Claude\claude_desktop_config.json`
- **macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`
- **Linux**: `~/.config/claude/claude_desktop_config.json`

## Available Tools

### 1. GetCurrentDatabase
Get the current database connection info with structured request logging.

**Parameters:** None

**Behavior:**
- Emits Serilog start/end events with a correlation ID and elapsed time
- Includes read-only security indicators in the payload

**Example Usage:**
```
Show me the current database connection info
```

### 2. SwitchDatabase
Switch to a different database on the same server with detailed audit logging.

**Parameters:**
- `databaseName` (string): The name of the database to switch to

**Behavior:**
- Validates connectivity before switching and logs success/failure details
- Captures correlation ID, elapsed time, and error information when the switch fails

**Example Usage:**
```
Switch to the Northwind database
```

### 3. GetDatabases
Get a list of all databases on the SQL Server instance with current database highlighted.

**Parameters:** None

**Example Usage:**
```
List all databases on this SQL Server instance
```

### 4. ExecuteQuery
Execute a SQL query on the current database.

**Parameters:**
- `query` (string): The SQL query to execute
 - `maxRows` (int, optional): Requested rows (defaults to 100; clamped to 100)

**Example Usage:**
```
Please execute "SELECT TOP 10 * FROM Users ORDER BY CreatedDate DESC" and show me the results
```

Notes:
- The server enforces a hard cap of 100 rows per query. Any higher request is clamped to 100.
- The server safely injects `TOP <N>` after the first `SELECT` when absent, and caps an existing `TOP` if it exceeds 100.

### 5. ReadQuery (SRS)
Execute a read-only T-SQL query with result formatting, per-call timeout, and parameter binding. Matches the SRS `read_query` specification.

**Parameters:**
- `query` (string, required): T-SQL `SELECT` statement
- `timeout` (int, optional): Per-call timeout in seconds; default 30; clamped 1–300
- `max_rows` (int, optional): Requested max rows; default 1000; clamped 1–10,000
- `format` (string, optional): `json` | `csv` | `table` (HTML); default `json`
- `parameters` (object, optional): Named parameters to bind (e.g., `{ id: 42 }`); keys may include or omit `@`
- `delimiter` (string, optional): CSV delimiter; default `,`; use `tab` or `\t` for tab

**Behavior:**
- Enforces read-only validation (SELECT-only, blocks DDL/DML/EXEC and multiple statements)
- Applies per-call timeout if provided; otherwise uses server default
- Injects or caps `TOP <N>` to respect `max_rows`
- Returns:
  - `json`: array of objects with explicit `null` values
  - `csv`: CSV string with header row and proper quoting
  - `table`: escaped HTML table with headers
- Includes `elapsed_ms`, `row_count`, and `columns` metadata (`name`, `data_type`, `allow_null`, `size`)

**Example Usage:**
```
ReadQuery query="SELECT * FROM Orders WHERE CustomerID = @id ORDER BY CreatedAt DESC" parameters={"id": 123} max_rows=500 format=csv delimiter="," timeout=60
```

### 5. GetTables
Get a list of all tables in the current database with row counts.

**Parameters:** None

**Example Usage:**
```
Show me all tables in the current database
```

### 6. GetTableSchema
Get the schema information for a specific table.

**Parameters:**
- `tableName` (string): Name of the table
- `schemaName` (string, optional): Schema name (defaults to "dbo")

**Example Usage:**
```
Get the schema for the Users table
```

### 7. GetStoredProcedures
Get a list of stored procedures in the current database.

**Parameters:** None

**Example Usage:**
```
List all stored procedures
```

### 8. GetStoredProcedureDetails
Get detailed information about a specific stored procedure including parameters, definition, and dependencies.

**Parameters:**
- `procedureName` (string): Name of the stored procedure
- `schemaName` (string, optional): Schema name (defaults to "dbo")

**Returns:**
- Procedure metadata (name, schema, create/modify dates)
- Complete list of parameters with data types, directions (input/output), and default values
- Full T-SQL definition of the procedure
- Dependencies (tables, views, and other objects referenced by the procedure)

**Example Usage:**
```
Get details for the stored procedure sp_GetUserOrders
```

```
Show me the parameters and definition of dbo.sp_CalculateRevenue
```

**Response includes:**
- `procedure_info` - Name, schema, dates, definition, and metadata
- `parameters` - Full parameter list with types, precision, scale, output flags
- `dependencies` - All database objects referenced by the procedure

### 9. GetObjectDefinition
Get detailed information about any database object (stored procedure, function, or view) in a unified endpoint.

**Parameters:**
- `objectName` (string): Name of the database object
- `schemaName` (string, optional): Schema name (defaults to "dbo")
- `objectType` (string, optional): Object type - 'PROCEDURE', 'FUNCTION', 'VIEW', or 'AUTO' to auto-detect (default is 'AUTO')

**Returns:**
- Object metadata (name, schema, type, create/modify dates, full definition)
- **For Procedures & Functions**: Complete parameter list with data types, directions, and default values
- **For Views**: Column information with data types and properties
- Dependencies (all database objects referenced)

**Example Usage:**
```
Get the definition of sp_GetCustomerOrders
```

```
Show me details for the view vw_SalesReport
```

```
Get information about the function fn_CalculateDiscount in the sales schema
```

**Key Features:**
- **Auto-detection**: Automatically determines if the object is a procedure, function, or view
- **Unified interface**: Single tool for all object types instead of separate tools
- **Comprehensive**: Returns object-specific information (parameters for procedures/functions, columns for views)
- **Full source code**: Includes complete T-SQL definition when available

**Response includes:**
- `object_info` - Name, schema, type, dates, and full T-SQL definition
- `parameters` - (For procedures/functions) Parameter details with types and directions
- `columns` - (For views) Column schema with data types and properties
- `dependencies` - All database objects referenced by this object

### 10. GetObjectDefinitions
Fetch definitions for multiple database objects in a single call with built-in limits and per-object status.

**Parameters:**
- `objectNames` (string): Comma- or newline-separated list of object names; schema can be supplied as `schema.object`
- `schemaName` (string, optional): Default schema name when not provided (defaults to "dbo")
- `objectType` (string, optional): 'PROCEDURE', 'FUNCTION', 'VIEW', or 'AUTO' (default) to auto-detect per object
- `maxObjects` (int, optional): Maximum number of objects to process (default 10, capped at 25)

**Behavior:**
- Processes distinct object names up to `maxObjects`
- Uses auto-detection unless a type is forced
- Returns per-object status (`OK` or `NOT_FOUND`) so missing objects don't block the batch
- Includes dependency, parameter (procedures/functions), and column (views) details per object

**Example Usage:**
```
GetObjectDefinitions objectNames="dbo.Users, dbo.Orders, reporting.vw_Sales" maxObjects=5
```

### 11. GetServerHealth
Check connectivity and return server properties.

**Parameters:** None

**Example Usage:**
```
Check server health and show properties
```
Returns:
- `server_name`, `product_version`, `product_level`, `edition`, `current_database`, `server_time`
- Status and connectivity indicators

## Connection String Examples

### Windows Authentication
```
Server=localhost;Database=YourDatabase;Trusted_Connection=true;TrustServerCertificate=true;
```

### SQL Server Authentication
```
Server=localhost;Database=YourDatabase;User Id=your_username;Password=your_password;TrustServerCertificate=true;
```

### Azure SQL Database
```
Server=your_server.database.windows.net;Database=YourDatabase;User Id=your_username@your_server;Password=your_password;Encrypt=true;TrustServerCertificate=false;
```

## Security Considerations

- This server executes SQL queries directly against your database
- Ensure proper database permissions are configured
- Use parameterized queries when possible to prevent SQL injection
- Consider limiting the database user's permissions to only what's necessary
- Never expose connection strings with passwords in version control

## Security, Hosting, and Tokenization Plan

### Current State

- The server already enforces **read-only query validation**, row limits, timeouts, and structured logging
- The current host is **stdio-only** (`WithStdioServerTransport()`), which is a good fit for local tools like Claude Desktop
- Connection strings are currently supplied through environment variables or `appsettings.json`

### Security Features Worth Adding Next

1. **Secret management**
   - Move production connection strings out of local config files and into a secret store such as Azure Key Vault, AWS Secrets Manager, GCP Secret Manager, or Docker/Kubernetes secrets
   - Support secret references so deployments do not need raw passwords in `claude_desktop_config.json` or environment exports

2. **Stronger identity and authorization**
   - Prefer **Azure AD / Entra ID**, managed identity, or another short-lived identity flow over long-lived SQL passwords where possible
   - Add an allowlist for approved databases, schemas, and optionally tables/views
   - Add a denylist or masking policy for sensitive columns such as passwords, tokens, SSNs, card numbers, and personal contact data

3. **Hosted MCP authentication**
   - If the server is exposed remotely, require **OAuth 2.1 / OIDC**, JWT bearer tokens, API keys behind a gateway, or mTLS
   - Bind every request to a caller identity and include that identity in audit logs
   - Add rate limiting, concurrency limits, and per-client quotas to reduce abuse

4. **Query governance**
   - Add configurable maximums for returned rows, execution time, and concurrent requests per client
   - Add optional approval or allowlist mode for free-form SQL in high-risk environments
   - Add query fingerprint logging so repeated risky access patterns are detectable without storing full sensitive payloads

5. **Data protection**
   - Add result redaction for known sensitive fields before they are returned to the MCP client
   - Prefer exposing tokenized or masked views instead of raw production tables
   - Consider separate read-only replicas or sanitized reporting databases for AI access

6. **Operational security**
   - Add audit fields for caller, server name, environment, database, operation, query hash, and elapsed time
   - Add alerting for blocked queries, repeated auth failures, and unusually expensive reads
   - Keep logs out of the MCP transport channel and continue using file or centralized structured logs only

### Can This MCP Be Hosted?

**Yes, but not in its current transport shape.**

Right now this project is set up for **local stdio hosting**, which works well for desktop MCP clients. To make it a hosted MCP service, the project would need:

- an **HTTP-capable MCP transport** (or an MCP-compatible gateway in front of the process)
- caller authentication and authorization
- TLS termination and network controls
- secret management for database credentials
- request throttling, audit logging, and deployment packaging

### Recommended Hosted Deployment Pattern

1. Keep the SQL Server login itself **read-only and least-privileged**
2. Containerize the server
3. Put it behind an API gateway / ingress with:
   - TLS
   - JWT or OAuth validation
   - rate limits
   - IP allowlists or private networking
4. Run it in a private network close to the database
5. Send logs and metrics to centralized monitoring

### Is Tokenization Possible?

**Yes, in two different senses:**

1. **Authentication tokens**
   - Very possible for a hosted version
   - Recommended approach: use short-lived access tokens (OIDC/JWT) issued by an identity provider, then validate them at the MCP edge or gateway

2. **Data tokenization**
   - Also possible, and highly recommended for sensitive datasets
   - Best approach for this project is to expose **tokenized views** or **masked reporting tables** instead of raw tables
   - For highly regulated fields, integrate with an external tokenization service and only let the MCP server read the already-tokenized values

### Similar Database MCP Patterns to Learn From

- **Microsoft SQL MCP Server / Azure Data API Builder MCP** - good reference for remote hosting, identity integration, and granular data exposure
- **Google Cloud managed MCP servers for databases** - good reference for hosted MCP, IAM-backed access, and managed observability
- **Oracle Database MCP Server** - good reference for auditability and least-privilege database accounts
- **AWS database MCP servers** - good reference for token-based cloud access, IAM, and centralized audit trails

Common patterns across those systems:

- hosted remote access is normal
- short-lived identity tokens are preferred over static passwords
- least-privilege database roles are mandatory
- audit logging and policy enforcement happen outside the LLM client as well as inside the database layer
- masking, tokenization, or sanitized replicas are strongly preferred for production data

### Suggested Implementation Plan

#### Phase 1 - Harden the current local/stdio server

- Keep stdio transport for local use
- Move connection strings to secret storage for non-local environments
- Add database/schema allowlists
- Add sensitive-column redaction rules
- Document a recommended read-only SQL role for deployments

#### Phase 2 - Prepare for hosted deployment

- Add or adopt an HTTP MCP transport
- Containerize the app
- Add health checks, readiness checks, and centralized telemetry
- Introduce gateway-based authentication, TLS, and rate limiting

#### Phase 3 - Add identity-aware access control

- Require JWT/OIDC tokens for remote callers
- Map caller identity to allowed environments/databases
- Add per-client quotas and better audit trails
- Add an option to disable free-form SQL and allow only curated inspection tools in high-security environments

#### Phase 4 - Protect sensitive production data

- Default to tokenized views, masked columns, or sanitized replicas
- Add field-level policy checks for common secrets and PII
- Review logs to ensure no sensitive result sets are stored

### Recommended Order of Work for This Repository

1. **First**: secret management + least-privilege SQL login
2. **Second**: database/schema allowlists + sensitive data redaction
3. **Third**: containerization + hosted transport proof of concept
4. **Fourth**: OIDC/JWT authentication and rate limiting
5. **Fifth**: tokenized views / masked datasets for production usage

This order keeps the current local workflow intact while making the project safer before it is exposed as a hosted MCP service.

## Error Handling

The server returns descriptive error messages for:
- Connection failures
- Invalid SQL syntax
- Permission issues
- Database not found

## Development

To modify or extend the server:

1. Add new methods to the `SqlServerTools` class
2. Decorate them with the `[McpServerTool]` attribute
3. Add `[Description]` attributes for parameters
4. Rebuild the project

Example new tool:
```csharp
[McpServerTool, Description("Get all stored procedures in the database")]
public static async Task<string> GetStoredProceduresAsync()
{
    // Implementation here
}
```

## Testing

To test the server locally:

1. Set up a local SQL Server instance
2. Configure the connection string
3. Run the server: `dotnet run`
4. Test with Claude Desktop or other MCP-compatible clients

## Dependencies

- `ModelContextProtocol` (1.2.0) - Official MCP C# SDK
- `Microsoft.Data.SqlClient` (7.0.0) - SQL Server connectivity
- `Microsoft.Extensions.Hosting` (10.0.5) - Host infrastructure

## License

This project is provided as-is for educational and development purposes.
## Logging

This project uses Serilog for structured logging.

- Default sink: JSON logs written to `stderr` (non-interfering with MCP stdio)
- Log content: start/end events with `correlation_id`, `operation`, `elapsed_ms`, plus error details
- Configuration: Serilog reads settings from `appsettings.json` if present

Example `appsettings.json` Serilog block:
```
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  }
}
```

Logs can be consumed directly from `stderr` or redirected as needed by your host environment.
