# MCP Client Configuration Samples

Copy the appropriate file to configure your MCP client.

| Client | File | Location |
|--------|------|----------|
| **Kilo** | `kilo.jsonc` | `.kilo/kilo.jsonc` (project) or merge into `~/.config/kilo/kilo.json` (global) |
| **VS Code** | `vscode-mcp.json` | `.vscode/mcp.json` (project) or VS Code user settings |
| **Claude Desktop** | `claude-desktop.json` | Windows: `%APPDATA%\Claude\claude_desktop_config.json`<br>macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`<br>Linux: `~/.config/claude/claude_desktop_config.json` |
| **Cursor / Windsurf** | `cursor-windsurf.json` | `mcp_settings.json` or `cline_mcp_settings.json` |
| **Server config** | `appsettings.json` | Place alongside executable or in project root |
| **Docker env** | `.env.example` | Copy to `.env` for `docker compose` |

## Connection String

Replace the placeholder values in each config with your actual SQL Server credentials:

```
Server=your_server,1433;Database=your_database;User Id=your_user;Password=your_password;TrustServerCertificate=true;
```

### Windows Authentication
```
Server=localhost;Database=YourDatabase;Trusted_Connection=true;TrustServerCertificate=true;
```

### Azure SQL
```
Server=your_server.database.windows.net;Database=YourDatabase;User Id=your_user@your_server;Password=your_password;Encrypt=true;
```

## Transport Options

| Transport | Config key | When to use |
|-----------|-----------|-------------|
| **SSE** (HTTP) | `"type": "sse"` or `"type": "remote"` | Docker deployment, remote servers, Kilo |
| **Stdio** (local) | `"type": "stdio"` or `"type": "local"` | Local `dotnet run`, Docker stdio bridge |
