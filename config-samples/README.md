# MCP Client Configuration Samples

Copy the appropriate file to configure your MCP client.

| Client | File | Location |
|--------|------|----------|
| **Kiro** | `kiro-mcp.json` | `.kiro/settings/mcp.json` (workspace) or `~/.kiro/settings/mcp.json` (user/global) |
| **Claude Desktop** | `claude-desktop.json` | Windows: `%APPDATA%\Claude\claude_desktop_config.json`<br>macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`<br>Linux: `~/.config/Claude/claude_desktop_config.json` |
| **VS Code** | `vscode-mcp.json` | `.vscode/mcp.json` (workspace) |
| **GitHub Copilot** (VS Code agent mode) | `github-copilot-mcp.json` | `.vscode/mcp.json` (workspace) or `mcp.servers` in user `settings.json` |
| **OpenAI Codex CLI** | `codex-config.toml` | `~/.codex/config.toml` |
| **Cursor / Windsurf** | `cursor-windsurf.json` | `mcp_settings.json` / `cline_mcp_settings.json` |
| **Kilo** | `kilo.jsonc` | `.kilo/kilo.jsonc` (project) |
| **Server config** | `appsettings.json` | Place alongside the executable or in the project root |
| **Docker env** | `.env.example` | Copy to `.env` for `docker compose` |

---

## Build first (for stdio / native .NET configs)

```bash
dotnet build -c Debug      # or: dotnet build -c Release
```

> **Important – DLL path:** `SqlServerMcpServer.csproj` sets `SelfContained` + `PublishSingleFile`,
> which forces a runtime-specific output folder. The runnable DLL is at:
>
> - Windows: `SqlServerMcpServer/bin/Debug/net10.0/win-x64/SqlServerMcpServer.dll`
> - Linux:   `SqlServerMcpServer/bin/Debug/net10.0/linux-x64/SqlServerMcpServer.dll`
>
> The plain `net10.0/SqlServerMcpServer.dll` (no RID subfolder) may be stale — always point your
> client at the RID-specific path. Update the path in each sample to match your machine.

> **Tip:** Prefer launching the built **DLL** (`dotnet path/to/SqlServerMcpServer.dll`) over
> `dotnet run`. With stdio transport, `dotnet run` can emit build/restore text on stdout and
> corrupt the JSON-RPC stream.

---

## Connection String

These samples use the local Docker test database:

```
Server=localhost,1433;Database=Optimum_Utilities;User Id=sa;password=YourStrong@Password123;Trusted_Connection=False;MultipleActiveResultSets=true;TrustServerCertificate=True;
```

Replace it with your own. Other examples:

```
# Windows Authentication
Server=localhost;Database=YourDatabase;Trusted_Connection=true;TrustServerCertificate=true;

# Azure SQL
Server=your_server.database.windows.net;Database=YourDatabase;User Id=your_user@your_server;Password=your_password;Encrypt=true;
```

> Security note: this server uses a **guarded-write** model (SELECT/INSERT/UPDATE allowed,
> DELETE/TRUNCATE require `confirm_unsafe_operation=true`, DDL always blocked). Use a
> least-privilege SQL login — a read-only login if you never want writes to be possible.

---

## Transport Options

| Transport | Config key | When to use |
|-----------|-----------|-------------|
| **Stdio** (local) | `"type": "stdio"` (or `"local"`) | Local DLL/Docker stdio bridge — the default |
| **SSE** (HTTP) | `"type": "sse"` / `"remote"`, `url` | Docker/remote deployment, Kilo |

Run the server in SSE mode with `MCP_TRANSPORT=sse` (default port 8080; the Docker compose maps `9090`).

---

## Tool names are snake_case

The MCP SDK exposes the C# methods using **snake_case** names. Use these exact names in any
`autoApprove` / allow lists (PascalCase will fail with "Unknown tool"):

```
get_connections, add_connection, switch_connection,
get_server_health, get_current_database, switch_database, get_databases,
get_tables, get_table_schema, get_stored_procedures, get_stored_procedure_details,
get_object_definition, get_object_definitions,
execute_query, read_query, search_table_data,
find_columns_by_data_type, find_tables_with_column, get_column_statistics,
get_table_relationships, get_index_information, get_index_fragmentation,
get_missing_indexes, get_query_execution_plan, get_wait_stats,
get_database_size, get_backup_history, get_error_log,
generate_model_class, get_cache_metrics, get_connection_pool_stats
```

---

## Quick verification

After configuring, ask the assistant: *"Check SQL server health"* (`get_server_health`) and
*"List the tables"* (`get_tables`). You should see your server version and table list returned.
