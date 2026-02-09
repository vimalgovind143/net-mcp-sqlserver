# SQL Server MCP Server

A Model Context Protocol (MCP) server that provides tools for interacting with Microsoft SQL Server databases, built with the official C# SDK.

## Features

- **🔒 Secure DML Support**: SELECT queries plus controlled INSERT, UPDATE, DELETE, and TRUNCATE operations
- **Confirmation Protection**: DELETE and TRUNCATE operations require explicit confirmation
- **DDL Protection**: CREATE, ALTER, DROP, and other schema-changing operations are blocked
- **Dynamic Database Switching**: Switch between databases on the same server without restarting
- **Database Listing**: View all available databases with current database highlighted
- **Execute SQL Queries**: Run SELECT, INSERT, UPDATE queries; DELETE/TRUNCATE with confirmation
- **List Tables**: Get all tables in the current database with row counts
- **Get Table Schema**: Retrieve column information for specific tables
- **Connection Info**: Display current database connection status
- **Stored Procedures**: List stored procedures with schema and dates, get detailed info including parameters, definitions, and dependencies
- **Object Definition**: Unified endpoint to get detailed information for any database object (procedures, functions, views) including definitions, parameters/columns, and dependencies
- **Health Check**: Verify connectivity and view server properties
- **Structured Logging**: JSON logs to stderr with correlation IDs and timings
- **Serilog Integration**: Structured logging via Serilog with JSON formatting
- **Row Limit Enforcement**: Max 100 rows returned per query

## 🔒 Security Features

This MCP server provides secure data access with controlled DML operations:

### **Query Classification**
Queries are classified into categories with different security levels:

| Category | Operations | Status |
|----------|------------|--------|
| **Read-Only** | SELECT, CTEs | ✅ Always Allowed |
| **Data Modification** | INSERT, UPDATE | ✅ Allowed with warnings |
| **Destructive** | DELETE, TRUNCATE | ⚠️ Requires Confirmation |
| **Schema Changes** | CREATE, ALTER, DROP | ❌ Always Blocked |

### **Blocked Operations (Always Prevented):**
- ❌ DROP, CREATE, ALTER statements (DDL)
- ❌ EXEC/EXECUTE stored procedures
- ❌ MERGE operations
- ❌ GRANT, REVOKE, DENY permissions
- ❌ BULK operations
- ❌ SELECT INTO (object creation)
- ❌ Multiple statements in a single request
- ❌ USE, SET, DBCC, BACKUP, RESTORE, RECONFIGURE, sp_configure

### **Allowed Operations:**
- ✅ SELECT queries for data retrieval
- ✅ INSERT statements (data creation)
- ✅ UPDATE statements (data modification)
- ✅ DELETE statements (with `confirmUnsafeOperation=true`)
- ✅ TRUNCATE statements (with `confirmUnsafeOperation=true`)
- ✅ Database listing and switching
- ✅ Table schema inspection
- ✅ Connection status queries

### **Confirmation Mechanism:**
For DELETE and TRUNCATE operations, you must explicitly confirm:

```json
// DELETE example - will FAIL without confirmation
{
  "query": "DELETE FROM Orders WHERE Status = 'Cancelled'",
  "confirmUnsafeOperation": false  // ❌ Blocked - confirmation required
}

// DELETE example - will SUCCEED with confirmation
{
  "query": "DELETE FROM Orders WHERE Status = 'Cancelled'",
  "confirmUnsafeOperation": true   // ✅ Allowed with confirmation
}
```

### **Error Messages:**
When a blocked operation is attempted, the server provides clear, specific error messages:

```
❌ CREATE operations are not allowed. This MCP server blocks DDL operations.
```

For DELETE/TRUNCATE without confirmation:
```
⚠️ DELETE operations require user confirmation. Set confirmUnsafeOperation=true to proceed.
```

### **Additional Safety Features:**
- Query timeout protection (configurable, default 30 seconds)
- Input validation and sanitization
- Detailed error reporting with helpful guidance
- Security mode indicators in all responses
- Warnings displayed for DML operations reminding users to have backups

## Dynamic Database Management

The MCP server supports dynamic database switching, allowing you to:

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

```json
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
- Includes security mode indicators in the payload

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
Execute a SQL query on the current database with pagination and metadata. Supports SELECT, INSERT, UPDATE; DELETE/TRUNCATE require confirmation.

**Parameters:**
- `query` (string): The SQL query to execute
- `maxRows` (int, optional): Maximum rows to return (default 100, max 1000)
- `offset` (int, optional): Offset for pagination (default: 0)
- `pageSize` (int, optional): Page size for pagination (default: 100, max: 1000)
- `includeStatistics` (bool, optional): Include query execution statistics (default: false)
- `confirmUnsafeOperation` (bool, optional): Confirm execution of DELETE/TRUNCATE operations (default: false)

**Example Usage:**
```
Please execute "SELECT TOP 10 * FROM Users ORDER BY CreatedDate DESC" and show me the results
```

**DML Examples:**
```
-- INSERT (allowed)
ExecuteQuery query="INSERT INTO Users (Name, Email) VALUES ('John', 'john@example.com')"

-- UPDATE (allowed)
ExecuteQuery query="UPDATE Users SET Status = 'Active' WHERE Id = 123"

-- DELETE (requires confirmation)
ExecuteQuery query="DELETE FROM Users WHERE Status = 'Inactive'" confirmUnsafeOperation=true

-- TRUNCATE (requires confirmation)
ExecuteQuery query="TRUNCATE TABLE TempData" confirmUnsafeOperation=true
```

Notes:
- The server enforces a hard cap of 100 rows per query. Any higher request is clamped to 100.
- The server safely injects `TOP <N>` after the first `SELECT` when absent, and caps an existing `TOP` if it exceeds 100.
- DELETE and TRUNCATE operations require `confirmUnsafeOperation=true` parameter.
- Warnings are displayed for DML operations to remind users to have backups.

### 5. ReadQuery (SRS)
Execute a SQL query with formatting and parameters (SRS: read_query). Supports SELECT, INSERT, UPDATE; DELETE/TRUNCATE require confirmation.

**Parameters:**
- `query` (string, required): T-SQL query
- `timeout` (int, optional): Per-call timeout in seconds; default 30; clamped 1–300
- `max_rows` (int, optional): Maximum rows to return (default 1000, range 1–10,000)
- `format` (string, optional): `json` | `csv` | `table` (HTML); default `json`
- `parameters` (object, optional): Named parameters to bind (e.g., `{ id: 42 }`); keys may include or omit `@`
- `delimiter` (string, optional): CSV delimiter; default `,`; use `tab` or `\t` for tab
- `confirm_unsafe_operation` (bool, optional): Confirm execution of DELETE/TRUNCATE operations (default: false)

**Behavior:**
- Enforces query validation (blocks DDL and dangerous operations)
- DELETE/TRUNCATE require confirmation flag
- Applies per-call timeout if provided; otherwise uses server default
- Injects or caps `TOP <N>` to respect `max_rows`
- Returns:
  - `json`: array of objects with explicit `null` values
  - `csv`: CSV string with header row and proper quoting
  - `table`: escaped HTML table with headers
- Includes `elapsed_ms`, `row_count`, and `columns` metadata (`name`, `data_type`, `allow_null`, `size`)

**Example Usage:**
```
-- SELECT with parameters
ReadQuery query="SELECT * FROM Orders WHERE CustomerID = @id ORDER BY CreatedAt DESC" parameters={"id": 123} max_rows=500 format=csv delimiter="," timeout=60

-- INSERT
ReadQuery query="INSERT INTO Logs (Message, CreatedAt) VALUES (@msg, GETDATE())" parameters={"msg": "System started"}

-- DELETE with confirmation
ReadQuery query="DELETE FROM OldLogs WHERE CreatedAt < @date" parameters={"date": "2023-01-01"} confirm_unsafe_operation=true
```

### 6. GetTables
Get a list of all tables in the current database with row counts.

**Parameters:** None

**Example Usage:**
```
Show me all tables in the current database
```

### 7. GetTableSchema
Get the schema information for a specific table.

**Parameters:**
- `tableName` (string): Name of the table
- `schemaName` (string, optional): Schema name (defaults to "dbo")

**Example Usage:**
```
Get the schema for the Users table
```

### 8. GetStoredProcedures
Get a list of stored procedures in the current database.

**Parameters:** None

**Example Usage:**
```
List all stored procedures
```

### 9. GetStoredProcedureDetails
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

### 10. GetObjectDefinition
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
- **DDL operations are blocked**: CREATE, ALTER, DROP, etc. cannot be executed
- **DML operations are allowed**: INSERT, UPDATE, DELETE, TRUNCATE can modify data
- **Destructive operations require confirmation**: DELETE and TRUNCATE need explicit `confirmUnsafeOperation=true`
- Ensure proper database permissions are configured
- Use parameterized queries when possible to prevent SQL injection
- Consider limiting the database user's permissions to only what's necessary
- Never expose connection strings with passwords in version control
- **Always have backups before executing DML operations**, especially DELETE and TRUNCATE

## Error Handling

The server returns descriptive error messages for:
- Connection failures
- Invalid SQL syntax
- Permission issues
- Database not found
- Blocked DDL operations
- Unconfirmed DELETE/TRUNCATE attempts

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

- `ModelContextProtocol` (0.4.1-preview.1) - Official MCP C# SDK
- `Microsoft.Data.SqlClient` (5.2.2) - SQL Server connectivity
- `Microsoft.Extensions.Hosting` (10.0.0) - Host infrastructure

## License

This project is provided as-is for educational and development purposes.

## Logging

This project uses Serilog for structured logging.

- Default sink: JSON logs written to file in the `logs` directory
- Log content: start/end events with `correlation_id`, `operation`, `elapsed_ms`, plus error details
- Configuration: Serilog reads settings from `appsettings.json` if present

Example `appsettings.json` Serilog block:
```json
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

Logs can be consumed from the `logs` directory or configured to use additional sinks as needed by your host environment.
