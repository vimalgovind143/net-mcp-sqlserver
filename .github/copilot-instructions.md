## Quick Context
- SQL Server MCP server built with the official C# SDK; `SqlServerMcpServer/Program.cs` wires Serilog and the stdio transport, then hosts the tools.
- All tools live in `SqlServerMcpServer/SqlServerMcpServer.cs` as static methods on `SqlServerTools` marked with `[McpServerTool]` plus `[Description]` on each parameter for MCP metadata.

## Runtime & Configuration
- Static fields cache `_currentConnectionString`, `_currentDatabase`, `_serverName`, `_environment`, and `_commandTimeout`; they hydrate from env vars first, then `appsettings.json` (searched in multiple locations), then defaults.
- `SwitchDatabase` vettes connectivity before swapping `_currentConnectionString`; reuse `CreateConnectionStringForDatabase` when you need to hop back to `master` (see `GetDatabasesAsync`).
- Respect the `SQLSERVER_COMMAND_TIMEOUT` override and keep new connections created with `_currentConnectionString` so the active database context persists between calls.

## Safety & Query Handling
- Every data-reading endpoint calls `IsReadOnlyQuery` before executing SQL; it strips comments, blocks multi-statements, DDL/DML keywords, and SELECT INTO. Do not bypass it for ad hoc SQL.
- `ExecuteQueryAsync` clamps the optional `maxRows` to ≤100 and relies on `ApplyTopLimit` to insert or cap `TOP` clauses; follow the same pattern for any new query-running tool.
- When a query is rejected or fails, mirror the structured error payload shape (`server_name`, `environment`, `database`, `security_mode`, etc.) so clients get consistent messaging.

## Logging & Responses
- Structured logging uses `LogStart`/`LogEnd` + `StderrJsonSink` so everything goes to `stderr` in JSON; keep stdout quiet (the startup banner in `Program.cs` is the lone exception).
- Return values are serialized with `JsonSerializer.Serialize(..., WriteIndented = true)`; include standard metadata fields (`server_name`, `environment`, `database`, `operation_type`) alongside domain data.

## Tool Implementation Patterns
- Wrap `SqlConnection`/`SqlCommand` in `using` and set `CommandTimeout = _commandTimeout`; this is visible in `GetTablesAsync`, `GetStoredProceduresAsync`, etc.
- Reuse helper methods (`CreateConnectionStringForDatabase`, `GetDatabaseFromConnectionString`) rather than hand-parsing connection strings.
- Shape new responses using existing examples: list-returning tools emit arrays of dictionaries, whereas detail endpoints (e.g. `GetStoredProcedureDetailsAsync`) return an object with `info`, `parameters`, `dependencies` sections.

## Developer Workflow
- Restore/build/run with standard `dotnet restore`, `dotnet build`, `dotnet run` from the repo root; the project auto-loads nearby `appsettings.json` files and honors environment overrides.
- Use `claude_desktop_config.json` and `NAMED_SERVERS.md` as runnable examples for wiring this MCP server into Claude Desktop or other MCP clients.

## When Extending
- Maintain read-only guarantees: if you introduce metadata queries that generate SQL, still validate via `IsReadOnlyQuery` unless the query is fully hard-coded.
- New tools should document parameter descriptions and update `README.md` so operators know the expanded surface area.
- Keep structured logging coverage: surround new work with `LogStart`/`LogEnd`, pass any contextual identifiers via the optional `context` argument, and bubble exceptions into the JSON response instead of throwing.
