## Overview
- `SqlServerMcpServer/Program.cs` boots the MCP host (`WithStdioServerTransport`) and wires Serilog to daily-rolled files under `SqlServerMcpServer/logs/mcp-server-*.log` (7-day retention, no console output beyond startup).
- All MCP tools live in `SqlServerMcpServer/SqlServerMcpServer.cs` as `[McpServerTool]` static methods on `SqlServerTools`; every parameter needs a `[Description]` attribute for MCP metadata.
- Static helpers (`CreateStandardResponse`, `CreateStandardErrorResponse`, `CreateStandardBlockedResponse`) centralize payload shape; new tools should lean on them for consistent metadata.

## State & Configuration
- `_currentConnectionString`, `_currentDatabase`, `_serverName`, `_environment`, and `_commandTimeout` hydrate in that file’s static constructor: environment variables take priority, then the first `appsettings.json` found in `AppContext.BaseDirectory`, `SqlServerMcpServer/appsettings.json`, or repo root, then defaults.
- `SwitchDatabase` always sanity-checks the target by opening a test `SqlConnection`; use `CreateConnectionStringForDatabase` to pivot to `master` (see `GetDatabasesAsync`).
- Honor the `SQLSERVER_COMMAND_TIMEOUT` override and ensure any new ADO calls set `CommandTimeout = _commandTimeout` so the active database context and timeout remain in sync.

## Logging & Telemetry
- Use `LogStart`/`LogEnd` to wrap tool bodies; they emit correlation IDs, operation names, and elapsed time via Serilog (file sink). Skip bespoke logging unless you extend these helpers.
- `Console.Error.WriteLine` is used only in the static constructor for early diagnostics; avoid additional console writes to keep the MCP transport clean.
- When catching exceptions, prefer `CreateStandardErrorResponse` so clients get `security_mode`, `operation`, and troubleshooting hints.

## SQL Safety & Query Limits
- The query tools (`ExecuteQueryAsync`, `ReadQueryAsync`) validate user SQL through `QueryValidator.IsDmlQueryAllowed`: SELECT/INSERT/UPDATE are allowed, DELETE/TRUNCATE require `confirmUnsafeOperation=true`, and DDL/EXEC/MERGE/BULK/permissions/`SELECT INTO`/multi-statements are always blocked.
- `QueryValidator.IsReadOnlyQuery` is the stricter SELECT-only validator; use it for paths that must never write (e.g. `PerformanceAnalysis.GetQueryExecutionPlan`). Every dynamic SQL path must pass through one of these validators unless the query text is fully hard-coded.
- `ExecuteQueryAsync` clamps `maxRows`, `pageSize`, and pagination to ≤1000 rows, then routes SELECTs through `ApplyPaginationAndLimit` to inject `TOP`/`OFFSET` on the **outer** SELECT (subqueries/CTEs/UNION branches untouched) while preserving existing limits—reuse that helper for any result-set tooling.
- Blocked queries return `BLOCKED_OPERATION` payloads with the original SQL and guidance; DELETE/TRUNCATE without confirmation report `security_mode: DML_WITH_CONFIRMATION`. Match that behavior if you add new validation rules.

## Response Shape & Metadata
- Standard success payloads include `server_name`, `environment`, `database`, `operation`, UTC `timestamp`, execution timing, `security_mode`, plus `data`/`metadata` collections—mirror this to keep clients parsable.
- List-oriented tools (`GetTablesAsync`, `GetDatabasesAsync`) embed filter echoes and row counts inside `data`; detail fetchers surface domain subsections like `procedure_info`, `parameters`, `dependencies`.
- Error responses set `security_mode` to `READ_ONLY_ENFORCED` and provide `troubleshooting_steps`; extend existing switch expressions (e.g., SQL error codes) rather than inventing new formats.

## Tool Implementation Patterns
- Always wrap `SqlConnection`/`SqlCommand` in `using` blocks and use the cached `_currentConnectionString`; this keeps the selected database sticky across calls.
- Prefer parameterized SQL for any user-provided filters (`nameFilter`, `schemaFilter`, etc.) and reuse the dynamic WHERE clause patterns already present.
- For aggregate metadata (counts, sizes, backups), follow `GetDatabasesAsync`/`GetTablesAsync` examples: compute summaries first, then enrich each row before serializing.

## Developer Workflow
- Build/run from the repo root with `dotnet restore`, `dotnet build`, `dotnet run`; `dotnet run --project SqlServerMcpServer/SqlServerMcpServer.csproj` works equivalently.
- `claude_desktop_config.json` and `NAMED_SERVERS.md` demonstrate wiring this MCP server into Claude Desktop or other MCP clients—update them when you add new tool surfaces.
- No automated tests exist; validate changes against a live SQL Server instance and watch the `logs/` folder for structured Serilog output when debugging.

## Extending Safely
- Route any new tool that runs user-supplied SQL through a validator: `IsDmlQueryAllowed` for general query tools, or `IsReadOnlyQuery` for paths that must stay strictly read-only. Hard-coded metadata SQL is also fine.
- Thread new Serilog context (like database names) through the optional `context` argument on `LogStart` when it aids debugging.
- Document new tools in `README.md` and ensure their payload schemas follow the established patterns so downstream MCP clients stay compatible.
