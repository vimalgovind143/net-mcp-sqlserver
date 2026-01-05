# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Initial project infrastructure and governance files

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

[Unreleased]: https://github.com/vimalgovind143/net-mcp-sqlserver/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/vimalgovind143/net-mcp-sqlserver/releases/tag/v1.0.0
