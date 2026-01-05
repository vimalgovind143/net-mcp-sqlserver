# Contributing to SQL Server MCP Server

Thank you for your interest in contributing to the SQL Server MCP Server! This document provides guidelines and information for contributors.

## Table of Contents

- [Welcome & Project Overview](#welcome--project-overview)
- [Getting Started](#getting-started)
- [Development Workflow](#development-workflow)
- [Coding Standards](#coding-standards)
- [Testing Requirements](#testing-requirements)
- [Adding New Tools](#adding-new-tools)
- [Security Guidelines](#security-guidelines)
- [Documentation](#documentation)
- [Release Process](#release-process)
- [Code of Conduct](#code-of-conduct)
- [Questions and Support](#questions-and-support)

## Welcome & Project Overview

The SQL Server MCP Server is a Model Context Protocol (MCP) server that provides read-only database tools for AI assistants. It's built with .NET 10.0 and the official MCP SDK, offering comprehensive SQL Server interaction capabilities while maintaining strict security through read-only enforcement.

For detailed project context, please refer to:
- [README.md](README.md) - Project overview, setup instructions, and usage examples
- [AGENTS.md](AGENTS.md) - Comprehensive technical architecture and coding standards

## Getting Started

### Prerequisites

- .NET 10.0 SDK
- SQL Server access (local or remote)
- Git
- Visual Studio 2022, Visual Studio Code, or JetBrains Rider (recommended)

### Setup Instructions

1. **Fork and Clone**
   ```bash
   git clone https://github.com/your-username/net-mcp-sqlserver.git
   cd net-mcp-sqlserver
   ```

2. **Restore Dependencies**
   ```bash
   dotnet restore
   ```

3. **Build the Project**
   ```bash
   dotnet build
   ```

4. **Run Tests**
   ```bash
   dotnet test
   ```

5. **Configure Database Connection**
   - Copy `appsettings.example.json` to `appsettings.json`
   - Update the connection string for your SQL Server instance
   - See README.md for detailed setup instructions

## Development Workflow

### Branch Naming Conventions

- `feature/feature-name` - New features
- `bugfix/bug-description` - Bug fixes
- `docs/documentation-update` - Documentation changes
- `refactor/code-improvement` - Code refactoring

### Commit Message Guidelines

We recommend using [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/):

```
type(scope): description

[optional body]

[optional footer(s)]
```

Examples:
- `feat(query): add support for CTE queries`
- `fix(validation): resolve null reference exception`
- `docs(readme): update installation instructions`

### Pull Request Process

1. Create a new branch from `main`
2. Make your changes with descriptive commits
3. Ensure all tests pass
4. Update documentation as needed
5. Create a pull request with:
   - Clear title and description
   - Reference any related issues
   - Screenshots for UI changes (if applicable)

### Code Review Expectations

- All pull requests require at least one review
- Reviews focus on: code quality, security, performance, and maintainability
- Address review feedback promptly
- Keep discussions constructive and respectful

## Coding Standards

This project follows comprehensive coding standards documented in [AGENTS.md](AGENTS.md). Key patterns include:

### Core Patterns

- **Nullable Reference Types**: Enabled throughout the project
- **Async/Await**: Use `async Task<string>` for database operations with `Async` suffix
- **Error Handling**: Use `ErrorHelper` and `ResponseFormatter` for consistent error responses
- **XML Documentation**: Required for all public APIs
- **Resource Disposal**: Always use `using` statements for disposable resources
- **SQL Security**: Always use parameterized queries to prevent SQL injection

### Code Style

- **Indentation**: 4 spaces (no tabs)
- **Bracing**: Allman style (braces on new lines)
- **Naming**: 
  - Public members: PascalCase
  - Private fields: camelCase with underscore prefix (`_cache`, `_metrics`)
  - Methods: PascalCase with `Async` suffix for async methods

### Example Implementation

```csharp
/// <summary>
/// Executes a read-only query and returns formatted results.
/// </summary>
/// <param name="query">The SQL query to execute.</param>
/// <returns>JSON-formatted response with query results.</returns>
[McpServerTool, Description("Tool description for AI")]
public static async Task<string> ExecuteQueryAsync(
    [Description("Parameter description")] string query)
{
    var sw = Stopwatch.StartNew();
    var corr = LoggingHelper.LogStart("ExecuteQuery", query);
    
    try
    {
        // Validate parameters
        if (string.IsNullOrWhiteSpace(query))
        {
            var context = new ErrorContext(ErrorCode.InvalidParameter, "Query required", "ExecuteQuery");
            return ResponseFormatter.ToJson(ResponseFormatter.CreateErrorContextResponse(context, 0));
        }
        
        using var connection = SqlConnectionManager.CreateConnection();
        await connection.OpenAsync();
        
        // Execute query with parameterization
        var sql = "SELECT * FROM table WHERE column = @param";
        using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = SqlConnectionManager.CommandTimeout
        };
        command.Parameters.AddWithValue("@param", value);
        
        // Process results
        var data = new { /* result */ };
        
        var payload = ResponseFormatter.CreateStandardResponse("ExecuteQuery", data, sw.ElapsedMilliseconds);
        LoggingHelper.LogEnd(corr, "ExecuteQuery", true, sw.ElapsedMilliseconds);
        return ResponseFormatter.ToJson(payload);
    }
    catch (SqlException sqlEx)
    {
        sw.Stop();
        var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "ExecuteQuery");
        LoggingHelper.LogEnd(Guid.Empty, "ExecuteQuery", false, sw.ElapsedMilliseconds, sqlEx.Message);
        return ResponseFormatter.ToJson(ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds));
    }
    catch (Exception ex)
    {
        sw.Stop();
        var context = ErrorHelper.CreateErrorContextFromException(ex, "ExecuteQuery");
        LoggingHelper.LogEnd(Guid.Empty, "ExecuteQuery", false, sw.ElapsedMilliseconds, ex.Message);
        return ResponseFormatter.ToJson(ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds));
    }
}
```

## Testing Requirements

### Test Framework

- **xUnit**: Test framework
- **Moq**: Mocking framework  
- **FluentAssertions**: Assertion library

### Test Structure

Tests are organized by the class they test (e.g., `QueryValidatorTests.cs` tests `QueryValidator.cs`).

### Test Naming Convention

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

### Coverage Requirements

- All public methods should have unit tests
- Edge cases and error conditions must be tested
- Security-critical code (query validation) requires comprehensive coverage
- Aim for >80% code coverage

### Running Tests

```bash
# Run all tests
dotnet test

# Run specific test file
dotnet test SqlServerMcpServer.Tests/QueryValidatorTests.cs

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## Adding New Tools

For detailed instructions, see the "Adding a New Tool" section in [AGENTS.md](AGENTS.md).

### Quick Overview

1. **Add Tool Method** to `SqlServerMcpServer.cs`:
   ```csharp
   [McpServerTool, Description("Tool description")]
   public static async Task<string> NewToolAsync(
       [Description("Param description")] string param)
   {
       return await OperationClass.NewToolImplementationAsync(param);
   }
   ```

2. **Implement** in appropriate Operations class following existing patterns
3. **Add Unit Tests** in `SqlServerMcpServer.Tests/`
4. **Update Documentation** (README.md, CHANGELOG.md)

### Required Components

- MCP tool registration with `[McpServerTool]` attribute
- Descriptive `[Description]` attributes for AI discoverability
- Structured error handling with `ErrorHelper`
- Consistent response formatting with `ResponseFormatter`
- Correlation-based logging with `LoggingHelper`
- Comprehensive unit tests

## Security Guidelines

### READ-ONLY Enforcement

This server enforces strict read-only access through `QueryValidator.cs`:

- **Allowed**: SELECT queries and CTEs (`WITH ... SELECT`)
- **Blocked**: DDL (CREATE, ALTER, DROP), DML (INSERT, UPDATE, DELETE), EXEC, system commands
- **Validation**: Comment stripping, normalization, keyword blocking, single-statement enforcement

### Security Requirements

- All database operations must be read-only
- Use parameterized queries to prevent SQL injection
- Validate all input parameters
- Never expose connection strings or sensitive data
- Follow principle of least privilege

### Query Validation Flow

1. Strip comments and normalize whitespace
2. Block dangerous keywords (DDL, DML, EXEC, etc.)
3. Enforce single statement rule
4. Require SELECT or CTE start
5. Generate warnings for potentially expensive queries

## Documentation

### Required Documentation Updates

- **README.md**: User-facing changes, new features, setup updates
- **AGENTS.md**: Architectural changes, new patterns, coding standard updates
- **CHANGELOG.md**: All notable changes following Keep a Changelog format
- **XML Comments**: All public APIs must have comprehensive documentation

### Documentation Standards

- Use clear, concise language
- Include code examples for new features
- Update relevant sections in existing documentation
- Follow existing formatting and style

## Release Process

The release process is documented in [.github/RELEASE_GUIDE.md](.github/RELEASE_GUIDE.md).

### Key Points

- **Semantic Versioning**: Follow MAJOR.MINOR.PATCH format
- **Automated Releases**: Tags trigger automated GitHub releases
- **Change Tracking**: Update CHANGELOG.md with each release
- **Version Bumping**: Update version numbers in project files

### Release Types

- **Major**: Breaking changes or significant new features
- **Minor**: New features in backward-compatible manner
- **Patch**: Bug fixes and minor improvements

## Code of Conduct

We are committed to providing a welcoming and inclusive environment for all contributors.

### Our Expectations

- Be respectful and considerate
- Use inclusive language
- Focus on constructive feedback
- Welcome newcomers and help them learn
- Assume good intentions

### Unacceptable Behavior

- Harassment or discrimination
- Personal attacks or insults
- Spam or irrelevant content
- Disruptive behavior

### Reporting Issues

If you experience or witness unacceptable behavior, please contact the project maintainers through GitHub Issues.

## Questions and Support

### Getting Help

- **GitHub Issues**: Report bugs and request features
- **GitHub Discussions**: Ask questions and share ideas (if enabled)
- **Documentation**: Check README.md and AGENTS.md first

### Issue Reporting

When reporting issues, please include:

- Clear description of the problem
- Steps to reproduce
- Expected vs. actual behavior
- Environment details (SQL Server version, .NET version)
- Relevant logs or error messages

### Feature Requests

For feature requests:

- Describe the use case and motivation
- Explain why it would be valuable
- Consider implementation approach (optional)
- Check if similar requests exist

## Thank You!

Your contributions help make the SQL Server MCP Server better for everyone. We appreciate your time and effort in improving this project!

---

For more detailed technical information, please refer to [AGENTS.md](AGENTS.md).
