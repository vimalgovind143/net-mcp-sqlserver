# AGENTS.md - SQL Server MCP Server Agent Instructions

## Project Overview

This is a **Model Context Protocol (MCP) Server** for SQL Server, built with the official C# SDK. It provides read-only tools for interacting with Microsoft SQL Server databases, designed for integration with AI assistants like Claude Desktop.

### Core Principles

- **READ-ONLY Security**: All operations enforce SELECT-only access - no data modifications allowed
- **Structured Responses**: All responses are JSON-formatted with consistent structure
- **Comprehensive Error Handling**: Rich error context with troubleshooting steps and suggestions
- **Caching**: Metadata caching with configurable TTLs for performance
- **Resilience**: Connection pooling with Polly retry/circuit breaker patterns

---

## Technology Stack

| Component | Technology | Version |
|-----------|-----------|---------|
| Runtime | .NET 10.0 | net10.0 |
| MCP SDK | ModelContextProtocol | 0.4.1-preview.1 |
| SQL Client | Microsoft.Data.SqlClient | 5.2.2 |
| Logging | Serilog | 2.12.0 |
| Caching | Microsoft.Extensions.Caching.Memory | 10.0.0 |
| Resilience | Polly | 8.2.1 |
| Testing | xUnit, Moq, FluentAssertions | Latest |

---

## Project Structure

```
net-mcp-sqlserver/
├── SqlServerMcpServer/                  # Main application
│   ├── Configuration/                   # Connection and pool management
│   │   ├── SqlConnectionManager.cs      # Connection string management
│   │   └── ConnectionPoolManager.cs     # Polly-based resilience
│   ├── Operations/                      # MCP tool implementations
│   │   ├── DatabaseOperations.cs        # Health check, DB listing/switching
│   │   ├── QueryExecution.cs            # Query execution (ExecuteQuery, ReadQuery)
│   │   ├── SchemaInspection.cs          # Tables, procedures, object definitions
│   │   ├── SchemaAnalysis.cs            # Relationships, indexes
│   │   ├── DataDiscovery.cs             # Column search, statistics
│   │   ├── PerformanceAnalysis.cs       # Wait stats, execution plans
│   │   ├── Diagnostics.cs               # Size, backup, error logs
│   │   └── CodeGeneration.cs            # C# model class generation
│   ├── Security/
│   │   └── QueryValidator.cs            # READ-ONLY enforcement
│   ├── Utilities/                       # Shared helpers
│   │   ├── CacheService.cs              # Memory cache with TTL
│   │   ├── ResponseFormatter.cs         # JSON response formatting
│   │   ├── ErrorHelper.cs               # Error context creation
│   │   ├── ErrorCode.cs                 # Error code enumeration
│   │   ├── ErrorContext.cs              # Error details container
│   │   ├── LoggingHelper.cs             # Correlation ID logging
│   │   ├── DataFormatter.cs             # Data formatting utilities
│   │   └── QueryFormatter.cs            # Query normalization
│   ├── SqlServerMcpServer.cs            # Main tool entry point
│   └── Program.cs                       # Host configuration
├── SqlServerMcpServer.Tests/            # Unit tests
└── SqlServerMcpServer.sln               # Solution file
```

---

## Architecture Patterns

### 1. MCP Tool Registration

Tools are registered using the `[McpServerTool]` attribute and `[Description]` attributes:

```csharp
[McpServerTool, Description("Tool description for AI")]
public static async Task<string> ToolNameAsync(
    [Description("Parameter description")] string param1,
    [Description("Optional parameter")] string? param2 = null)
{
    // Implementation
}
```

### 2. Response Pattern
All responses use `ResponseFormatter` for consistent JSON structure:

```csharp
// Success response
var payload = ResponseFormatter.CreateStandardResponse("OperationName", data, elapsedMs);
return ResponseFormatter.ToJson(payload);

// Error response
var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "OperationName");
var response = ResponseFormatter.CreateErrorContextResponse(context, elapsedMs);
return ResponseFormatter.ToJson(response);
```

### 3. Error Handling Pattern

Always use `ErrorHelper` to create rich error contexts:

```csharp
try
{
    // Operation logic
}
catch (SqlException sqlEx)
{
    var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "OperationName");
    // Add context-specific guidance
    context.SuggestedFixes.Add("Specific suggestion");
    var response = ResponseFormatter.CreateErrorContextResponse(context, elapsedMs);
    return ResponseFormatter.ToJson(response);
}
catch (Exception ex)
{
    var context = ErrorHelper.CreateErrorContextFromException(ex, "OperationName");
    var response = ResponseFormatter.CreateErrorContextResponse(context, elapsedMs);
    return ResponseFormatter.ToJson(response);
}
```

### 4. Logging Pattern

Use `LoggingHelper` for correlation-based logging:

```csharp
var sw = Stopwatch.StartNew();
var corr = LoggingHelper.LogStart("OperationName", "context info");
try
{
    // Operation
    LoggingHelper.LogEnd(corr, "OperationName", true, sw.ElapsedMilliseconds);
}
catch
{
    LoggingHelper.LogEnd(corr, "OperationName", false, sw.ElapsedMilliseconds, ex.Message);
}
```

---

## Security Guidelines

### Validation Flow (QueryValidator.IsReadOnlyQuery)

- Strips block/line comments, normalizes whitespace, uppercases the query.
- Blocks multiple statements (allows only a single trailing semicolon).
- Blocks dangerous keywords:
  - **DDL**: CREATE, ALTER, DROP, TRUNCATE
  - **DML**: INSERT, UPDATE, DELETE, MERGE
  - **Execution**: EXEC, EXECUTE, BULK
  - **Permissions**: GRANT, REVOKE, DENY
  - **System/Config**: USE, SET, DBCC, BACKUP, RESTORE, RECONFIGURE, SP_CONFIGURE
  - **Object Creation**: SELECT INTO
- Requires queries to start with SELECT or CTEs (`WITH ... SELECT`); otherwise blocked as NON_SELECT_STATEMENT.

### Allowed Operations

- SELECT queries (with CTEs via WITH) that do not include blocked operations
- Database listing and switching
- Schema inspection
- Table/procedure/view introspection

### Query Warnings (QueryValidator.GenerateQueryWarnings)

- Adds advisory warnings (not blockers) when:
  - No WHERE/TOP clause is present (may return large result set)
  - Manual pagination detected (offset provided without OFFSET/FETCH clause)

---

## Coding Standards

### 1. Nullable Reference Types
The project uses nullable reference types. Always handle nulls appropriately:
```csharp
string? schemaName = "dbo"  // Nullable parameter with default
```

### 2. Async/Await

- Use `async Task<string>` for database operations
- Use `Async` suffix for async methods
- Always use `ConfigureAwait(false)` is NOT required (console app)

### 3. Parameter Validation

Validate parameters early and return structured errors:
```csharp
if (string.IsNullOrWhiteSpace(tableName))
{
    var context = new ErrorContext(ErrorCode.InvalidParameter, "Table name required", "Operation");
    return ResponseFormatter.ToJson(ResponseFormatter.CreateErrorContextResponse(context, 0));
}
```

### 4. SQL Parameters

Always use parameterized queries to prevent SQL injection:
```csharp
command.Parameters.AddWithValue("@param", value);
```

### 5. Resource Disposal

Use `using` statements for all disposable resources:
```csharp
using var connection = SqlConnectionManager.CreateConnection();
using var command = new SqlCommand(query, connection);
using var reader = await command.ExecuteReaderAsync();
```

---

## Testing Guidelines

### Test Structure

Tests are organized by the class they test (e.g., `QueryValidatorTests.cs` tests `QueryValidator.cs`).

### Testing Frameworks

- **xUnit**: Test framework
- **Moq**: Mocking framework
- **FluentAssertions**: Assertion library

### Test Patterns

```csharp
[Fact]
public void MethodName_Condition_ExpectedResult()
{
    // Arrange
    var input = "test";
    
    // Act
    var result = MethodUnderTest(input);
    
    // Assert
    result.Should().BeTrue();
}
```

### Running Tests

```bash
dotnet test
```

---

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `SQLSERVER_CONNECTION_STRING` | Database connection string | localhost/master |
| `SQLSERVER_COMMAND_TIMEOUT` | Command timeout in seconds | 30 |
| `MCP_SERVER_NAME` | Server identifier | "SQL Server MCP" |
| `MCP_ENVIRONMENT` | Environment name | "unknown" |
| `CACHE_TTL_METADATA_SECONDS` | Metadata cache TTL | 300 |
| `CACHE_TTL_SCHEMA_SECONDS` | Schema cache TTL | 600 |
| `CACHE_TTL_PROCEDURE_SECONDS` | Procedure cache TTL | 300 |

### Configuration Precedence

1. Environment variables (highest)
2. appsettings.json
3. Built-in defaults (lowest)

---

## Common Tasks

### Adding a New Tool

1. **Add method to `SqlServerMcpServer.cs`** (facade):
```csharp
[McpServerTool, Description("Tool description")]
public static async Task<string> NewToolAsync(
    [Description("Param description")] string param)
{
    return await OperationClass.NewToolImplementationAsync(param);
}
```

2. **Implement in appropriate Operations class**:
```csharp
public static async Task<string> NewToolImplementationAsync(string param)
{
    var sw = Stopwatch.StartNew();
    try
    {
        var corr = LoggingHelper.LogStart("NewTool", param);
        
        // Validate parameters
        if (string.IsNullOrWhiteSpace(param))
        {
            var context = new ErrorContext(ErrorCode.InvalidParameter, "...", "NewTool");
            return ResponseFormatter.ToJson(ResponseFormatter.CreateErrorContextResponse(context, 0));
        }
        
        using var connection = SqlConnectionManager.CreateConnection();
        await connection.OpenAsync();
        
        // Execute query
        var query = "SELECT ...";
        using var command = new SqlCommand(query, connection)
        {
            CommandTimeout = SqlConnectionManager.CommandTimeout
        };
        
        // Process results
        var data = new { /* result */ };
        
        var payload = ResponseFormatter.CreateStandardResponse("NewTool", data, sw.ElapsedMilliseconds);
        LoggingHelper.LogEnd(corr, "NewTool", true, sw.ElapsedMilliseconds);
        return ResponseFormatter.ToJson(payload);
    }
    catch (SqlException sqlEx)
    {
        sw.Stop();
        var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "NewTool");
        LoggingHelper.LogEnd(Guid.Empty, "NewTool", false, sw.ElapsedMilliseconds, sqlEx.Message);
        return ResponseFormatter.ToJson(ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds));
    }
    catch (Exception ex)
    {
        sw.Stop();
        var context = ErrorHelper.CreateErrorContextFromException(ex, "NewTool");
        LoggingHelper.LogEnd(Guid.Empty, "NewTool", false, sw.ElapsedMilliseconds, ex.Message);
        return ResponseFormatter.ToJson(ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds));
    }
}
```

3. **Add unit tests** in `SqlServerMcpServer.Tests/`

### Modifying Query Validation

Edit `Security/QueryValidator.cs`:
- `IsReadOnlyQuery()` - Add/modify blocked keywords
- `GetBlockedOperationMessage()` - Add error messages

### Adding New Error Codes

1. Add to `Utilities/ErrorCode.cs`
2. Add mapping in `ErrorHelper.MapSqlErrorNumber()`
3. Add suggestions and troubleshooting steps

---

## Build and Run

### Build
```bash
dotnet build
```

### Run
```bash
dotnet run --project SqlServerMcpServer
```

### Test
```bash
dotnet test
```

### Publish
```bash
dotnet publish -c Release -o publish
```

---

## Important Notes for AI Agents

1. **Never modify data** - This is a READ-ONLY server. Never suggest INSERT/UPDATE/DELETE operations.

2. **Always use ResponseFormatter** - All responses must go through `ResponseFormatter.ToJson()`.

3. **Always handle errors** - Every operation must have try-catch with proper error context creation.

4. **Validate before executing** - Use `QueryValidator.IsReadOnlyQuery()` for any user-provided SQL.

5. **Use caching appropriately** - Check `CacheService` for reusable patterns.

6. **Log operations** - Use `LoggingHelper.LogStart/LogEnd` pattern for all operations.

7. **Parameter safety** - Always use SQL parameters, never string concatenation for user input.

8. **Test changes** - Run `dotnet build` and `dotnet test` after changes.

9. **Follow naming** - Async methods end with `Async`, tools in `SqlServerMcpServer.cs` delegate to Operations classes.

10. **JSON responses** - All tool responses are JSON strings with consistent structure including `server_name`, `database`, `security_mode`, etc.
