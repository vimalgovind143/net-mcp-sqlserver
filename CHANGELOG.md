# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.3.0] - 2026-05-29

### Added
- MCP configuration samples for multiple clients: Kiro, Claude Desktop, VS Code, GitHub Copilot, Codex, Cursor/Windsurf
- Comprehensive configuration guide with build/DLL-path gotchas and tool name reference

### Changed
- Upgraded dependencies to latest stable releases and removed unstable dev/preview builds:
  - `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Configuration.Json`, and
    `Microsoft.Extensions.Caching.Memory` moved off `11.0.0-preview` builds; these are now
    provided by the `net10.0` shared framework (explicit references removed, resolving NU1510)
  - `Serilog` `4.3.2-dev` → `4.3.1`, `Serilog.Settings.Configuration` `10.0.1-dev` → `10.0.0`,
    `Serilog.Sinks.File` `8.0.0-nblumhardt` → `7.0.0` (stable)
  - Test tooling: `Microsoft.NET.Test.Sdk` `18.0.1` → `18.6.0`, `FluentAssertions` `8.8.0` → `8.10.0`,
    `coverlet.collector` `6.0.4` → `10.0.1`
- Verified clean build and full test suite (325 tests passing) after the upgrade

### Fixed
- Row-limit injection (`QueryFormatter.ApplyTopLimit` / `ApplyPaginationAndLimit`) now applies
  `TOP`/`OFFSET-FETCH` to the **outer** SELECT only. Previously the regex rewrote every SELECT,
  injecting `TOP` into subqueries, CTE bodies, and UNION branches and silently altering results.
- `ReadQueryAsync` now enforces `max_rows` while reading the result set (defensive backstop in
  addition to `TOP` injection), preventing unbounded row materialization.
- Added regression tests for subquery / CTE / UNION / pagination row-limit handling.
- `GetCurrentDatabase` now reports `security_mode: GUARDED_WRITE` with accurate
  `allowed_operations` / `blocked_operations` lists instead of the stale `READ_ONLY` /
  "SELECT queries only" payload.
- CI/CD workflow syntax error: removed stray `</parameter>` tag from dotnet-build.yml test command

### Documentation
- Realigned docs with the actual guarded-write security model (code is the source of truth):
  SELECT/INSERT/UPDATE allowed, DELETE/TRUNCATE gated behind `confirmUnsafeOperation`, and
  DDL/administrative statements always blocked. Updated `README.md`, `AGENTS.md`,
  `CONTRIBUTING.md`, `.github/copilot-instructions.md`, and `NAMED_SERVERS.md`, which previously
  described the server as strictly read-only.

## [1.0.0] - 2025-12-06

### Added
- SQL Server MCP Server with Model Context Protocol SDK integration
- Read-only database tools for AI assistants
- Comprehensive SQL Server operations: health checks, query execution, schema inspection
- Security layer with query validation enforcing SELECT-only access
- Performance analysis tools: wait stats, execution plans, index analysis
- Data discovery features: column search, statistics generation
- Code generation for C# model classes from database schemas
- Comprehensive error handling with rich error contexts
- Caching layer with configurable TTLs for performance optimization
- Connection pooling with Polly retry/circuit breaker patterns
- Structured JSON responses with consistent formatting
- Comprehensive unit test suite with xUnit, Moq, and FluentAssertions
- CI/CD workflows with automated testing and releases
- Docker support for SQL Server 2022 testing environments

[Unreleased]: https://github.com/vimalgovind143/net-mcp-sqlserver/compare/v1.3.0...HEAD
[1.3.0]: https://github.com/vimalgovind143/net-mcp-sqlserver/compare/v1.0.0...v1.3.0
[1.0.0]: https://github.com/vimalgovind143/net-mcp-sqlserver/releases/tag/v1.0.0
