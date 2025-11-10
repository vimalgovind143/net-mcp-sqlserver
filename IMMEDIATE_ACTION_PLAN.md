
# SQL Server MCP Server - Immediate Action Plan

**Created**: November 10, 2025  
**Timeline**: Next 2 Weeks  
**Focus**: Complete Phase 1 Critical Infrastructure  

---

## Executive Summary

Based on the comprehensive status assessment, this document outlines the immediate actions required to complete Phase 1 of the improvement plan. The focus is on addressing critical gaps in testing infrastructure, security hardening, and configuration management that are essential for production readiness.

---

## Priority Matrix

| Priority | Impact | Effort | Timeline | Status |
|----------|--------|--------|----------|---------|
| P1: CI/CD Pipeline | High | Low | 1-2 days | 🔴 Not Started |
| P1: Test Dependencies | High | Low | 1 day | 🔴 Not Started |
| P1: Connection String Security | High | Medium | 2-3 days | 🔴 Not Started |
| P2: Configuration Management | High | Medium | 3-4 days | 🔴 Not Started |
| P2: Rate Limiting | Medium | Medium | 3-4 days | 🔴 Not Started |
| P2: Integration Tests | Medium | High | 5-7 days | 🔴 Not Started |

---

## Week 1 Action Items (November 10-16, 2025)

### Day 1-2: CI/CD Pipeline & Test Infrastructure

#### 1.1 Update CI/CD Pipeline
**File**: `.github/workflows/dotnet-build.yml`

**Actions**:
- Add test execution step
- Add code coverage reporting
- Set coverage threshold (70% initial target)
- Add test result publishing

**Implementation**:
```yaml
- name: Test
  run: dotnet test SqlServerMcpServer.Tests/SqlServerMcpServer.Tests.csproj --configuration Release --no-build --verbosity normal --collect:"XPlat Code Coverage"

- name: Upload Coverage Reports
  uses: codecov/codecov-action@v3
  with:
    file: ./SqlServerMcpServer.Tests/TestResults/**/*.coverage.xml
    flags: unittests
    name: codecov-umbrella
```

#### 1.2 Add Missing Test Dependencies
**File**: `SqlServerMcpServer.Tests/SqlServerMcpServer.Tests.csproj`

**Actions**:
- Add Moq package for mocking
- Add FluentAssertions for readable assertions
- Update package versions if needed

**Implementation**:
```xml
<PackageReference Include="Moq" Version="4.20.69" />
<PackageReference Include="FluentAssertions" Version="6.12.0" />
```

### Day 3-4: Security Hardening

#### 1.3 Connection String Sanitization
**New File**: `SqlServerMcpServer/Security/ConnectionStringHelper.cs`

**Actions**:
- Create `ConnectionStringHelper` class
- Implement password redaction for logging
- Update all logging statements in `SqlConnectionManager`

**Key Methods**:
```csharp
public static string SanitizeForLogging(string connectionString)
public static string RedactPassword(string connectionString)
public static bool ContainsSensitiveInfo(string connectionString)
```

#### 1.4 Basic Rate Limiting
**New File**: `SqlServerMcpServer/Security/RateLimiter.cs`

**Actions**:
- Implement `RateLimiter` class
- Add configurable limits per operation
- Return 429 status when limit exceeded
- Integrate with query execution pipeline

**Key Features**:
- Per-client rate limiting
- Sliding window algorithm
- Configurable limits via configuration
- Memory-efficient implementation

---

## Week 2 Action Items (November 17-23, 2025)

### Day 5-7: Configuration Management

#### 2.1 Create Configuration Class
**New File**: `SqlServerMcpServer/Configuration/SqlServerConfiguration.cs`

**Actions**:
- Create `SqlServerConfiguration` class with validation attributes
- Define all configuration properties
- Add validation methods

**Key Properties**:
```csharp
public class SqlServerConfiguration
{
    public string ConnectionString { get; set; }
    public int CommandTimeout { get; set; } = 30;
    public int MaxQueryResults { get; set; } = 1000;
    public bool EnableQueryCache { get; set; } = true;
    public TimeSpan CacheTTL { get; set; } = TimeSpan.FromMinutes(5);
    public RateLimitConfiguration RateLimiting { get; set; }
}
```

#### 2.2 Implement Options Pattern
**File**: `SqlServerMcpServer/Program.cs`

**Actions**:
- Configure options pattern
- Add configuration validation on startup
- Update `SqlConnectionManager` to use `IOptions<SqlServerConfiguration>`

#### 2.3 Create Example Configuration
**New File**: `SqlServerMcpServer/appsettings.example.json`

**Actions**:
- Document all configuration options
- Provide sensible defaults
- Add comments explaining each setting

### Day 8-10: Testing & Quality

#### 2.4 Add Integration Tests
**New Files**:
- `SqlServerMcpServer.Tests/Integration/DatabaseIntegrationTests.cs`
- `SqlServerMcpServer.Tests/Integration/ConnectionSwitchingTests.cs`

**Actions**:
- Setup test database with sample data
- Test actual database operations
- Test connection switching scenarios
- Use Docker container for SQL Server testing

#### 2.5 Set Coverage Thresholds
**File**: `SqlServerMcpServer.Tests/SqlServerMcpServer.Tests.csproj`

**Actions**:
- Configure coverlet settings
- Set minimum coverage threshold (70%)
- Exclude auto-generated code from coverage

**Implementation**:
```xml
<ItemGroup>
  <CoverletSettings Include="SqlServerMcpServer.Tests">
    <CoverageThreshold>70</CoverageThreshold>
    <Exclude>[*]*</Exclude>
    <Exclude>SqlServerMcpServer.Program</Exclude>
  </CoverletSettings>
</ItemGroup>
```

#### 2.6 Error Response Standardization
**New File**: `SqlServerMcpServer/Utilities/ErrorResponse.cs`

**Actions**:
- Create `ErrorResponse` class
- Add error codes enum
- Include troubleshooting hints
- Update all error handling to use standardized format

---

## Implementation Checklist

### Week 1 Checklist
- [ ] Update `.github/workflows/dotnet-build.yml` with test execution
- [ ] Add code coverage reporting to CI/CD
- [ ] Add Moq and FluentAssertions packages
- [ ] Create `ConnectionStringHelper` class
- [ ] Implement password redaction
- [ ] Update logging statements to use sanitized strings
- [ ] Create `RateLimiter` class
- [ ] Integrate rate limiting with query execution
- [ ] Add rate limiting configuration

### Week 2 Checklist
- [ ] Create `SqlServerConfiguration` class
- [ ] Add validation attributes
- [ ] Implement options pattern in `Program.cs`
- [ ] Update `SqlConnectionManager` to use DI
- [ ] Create `appsettings.example.json`
- [ ] Set up integration test infrastructure
- [ ] Write database integration tests
- [ ] Configure code coverage thresholds
- [ ] Create `ErrorResponse` class
- [ ] Standardize all error responses

---

## Success Criteria

### Week 1 Success Metrics
- ✅ CI/CD pipeline executes tests on every push/PR
- ✅ Code coverage reports are generated and published
- ✅ Connection strings are sanitized in all logs
- ✅ Basic rate limiting is functional
- ✅ All tests pass with new dependencies

### Week 2 Success Metrics
- ✅ Configuration management is fully implemented
- ✅ Integration tests cover critical database operations
- ✅ Code coverage threshold is enforced (≥70%)
- ✅ Error responses are standardized and consistent
- ✅ Phase 1 is 100% complete

---

## Risk Mitigation

### Technical Risks
- **Breaking Changes**: Minimize by maintaining backward compatibility
- **Test Failures**: Address incrementally, focus on critical path first
- **Performance Impact**: Monitor query execution times with new features

### Timeline Risks
- **Scope Creep**: Stick to defined Phase 1 items only
- **Dependencies**: All dependencies are available via NuGet
- **Resource Constraints**: Focus on highest impact items first

---

## Next Steps After Week 2

1. **Phase 1 Completion Review**: Assess all Phase 1 items
2. **Phase 2 Planning**: Begin performance and reliability improvements
3. **Documentation Update**: Update README with new configuration options
4. **Release Preparation**: Prepare for first production-ready release

---

## Resources Required

### Development Resources
- 1 developer full-time for 2 weeks
- Access to SQL Server for integration testing
- Docker environment for consistent testing

### Tools & Services
- GitHub Actions (already available)
- Codecov for coverage reporting (free tier sufficient)
- NuGet packages (all open source)

---

**Document Status**: Ready for Implementation
**Next Review**: November 17, 2025 (Week 1 Progress Check)
**Final Review**: November 24, 2025 (Phase 1 Completion)