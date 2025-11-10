# SQL Server MCP Server - Additional Operations Design

## Document Information
- **Version**: 1.0
- **Last Updated**: November 10, 2025
- **Status**: Design Phase

---

## Table of Contents
1. [Overview](#overview)
2. [Design Principles](#design-principles)
3. [Operation Categories](#operation-categories)
4. [Priority Operations](#priority-operations)
5. [Implementation Guidelines](#implementation-guidelines)
6. [API Patterns](#api-patterns)

---

## Overview

This document defines additional read-only operations to enhance the SQL Server MCP Server's capabilities in database analysis, performance monitoring, and development workflows.

### Goals
- Expand analytical capabilities while maintaining read-only security
- Provide actionable insights for database optimization
- Enable efficient data discovery and exploration
- Support development workflows with code generation

---

## Design Principles

### 1. Read-Only First
- All operations must be strictly read-only
- No data modification, even temporary tables
- Use appropriate isolation levels

### 2. Performance Conscious
- Set reasonable default limits (e.g., max 1000 rows)
- Provide timeout configurations
- Cache metadata where appropriate

### 3. Consistent API Design
- Uniform parameter naming conventions
- Standardized response formats
- Consistent error handling

### 4. Security Aware
- Respect database permissions
- Sanitize all outputs
- Rate limiting on expensive operations

### 5. User-Friendly
- Clear, descriptive operation names
- Helpful error messages
- Include usage examples

---

## Operation Categories

### Category 1: Schema Analysis (Priority: High)
- GetTableRelationships
- GetIndexInformation
- GetConstraints
- GetTableDependencies

### Category 2: Performance Analysis (Priority: High)
- GetMissingIndexes
- GetQueryExecutionPlan
- GetIndexFragmentation
- GetWaitStats

### Category 3: Data Discovery (Priority: Medium)
- SearchTableData
- GetColumnStatistics
- FindColumnsByDataType
- FindTablesWithColumn

### Category 4: Code Generation (Priority: Low)
- GenerateModelClass
- GenerateInsertStatements
- GenerateCrudProcedures

### Category 5: Diagnostics (Priority: Low)
- GetDatabaseSize
- GetBackupHistory
- GetErrorLog

---

## Priority Operations

## 1. GetTableRelationships

**Purpose**: Discover foreign key relationships between tables

**Parameters**:
- `tableName` (string, optional)
- `schemaName` (string, optional, default: 'dbo')
- `includeReferencedBy` (boolean, optional, default: true)
- `includeReferences` (boolean, optional, default: true)

**Response**:
```json
{
  "server_name": "string",
  "database": "string",
  "relationships": [
    {
      "constraint_name": "string",
      "relationship_type": "REFERENCES|REFERENCED_BY",
      "parent_table": "string",
      "parent_columns": ["string"],
      "child_table": "string",
      "child_columns": ["string"],
      "update_rule": "string",
      "delete_rule": "string"
    }
  ]
}
```

---

## 2. GetIndexInformation

**Purpose**: List indexes with usage statistics

**Parameters**:
- `tableName` (string, optional)
- `schemaName` (string, optional, default: 'dbo')
- `includeStatistics` (boolean, optional, default: true)

**Response**:
```json
{
  "indexes": [
    {
      "table_name": "string",
      "index_name": "string",
      "index_type": "string",
      "is_unique": "boolean",
      "key_columns": ["string"],
      "included_columns": ["string"],
      "user_seeks": "integer",
      "user_scans": "integer",
      "size_mb": "decimal"
    }
  ]
}
```

---

## 3. GetMissingIndexes

**Purpose**: SQL Server's missing index suggestions

**Parameters**:
- `tableName` (string, optional)
- `minImpact` (decimal, optional, default: 10.0)
- `topN` (integer, optional, default: 20)

**Response**:
```json
{
  "missing_indexes": [
    {
      "table_name": "string",
      "equality_columns": ["string"],
      "inequality_columns": ["string"],
      "included_columns": ["string"],
      "impact_score": "decimal",
      "create_index_statement": "string"
    }
  ]
}
```

---

## 4. GetQueryExecutionPlan

**Purpose**: Retrieve execution plan without executing

**Parameters**:
- `query` (string, required)
- `planType` (string, optional: 'ESTIMATED'|'SHOWPLAN_XML')
- `includeAnalysis` (boolean, optional, default: true)

**Response**:
```json
{
  "execution_plan": {
    "plan_xml": "string",
    "estimated_cost": "decimal",
    "estimated_rows": "integer"
  },
  "analysis": {
    "warnings": ["string"],
    "recommendations": ["string"],
    "expensive_operators": []
  }
}
```

---

## 5. GetColumnStatistics

**Purpose**: Statistical analysis of column data

**Parameters**:
- `tableName` (string, required)
- `schemaName` (string, optional, default: 'dbo')
- `columnName` (string, optional)
- `topNValues` (integer, optional, default: 10)

**Response**:
```json
{
  "columns": [
    {
      "column_name": "string",
      "total_rows": "integer",
      "null_count": "integer",
      "distinct_count": "integer",
      "numeric_stats": {
        "min_value": "decimal",
        "max_value": "decimal",
        "avg_value": "decimal"
      },
      "top_values": [
        {"value": "string", "count": "integer"}
      ]
    }
  ]
}
```

---

## 6. SearchTableData

**Purpose**: Full-text search across tables

**Parameters**:
- `searchTerm` (string, required)
- `tableName` (string, optional)
- `maxResults` (integer, optional, default: 100)

**Response**:
```json
{
  "matches": [
    {
      "table_name": "string",
      "column_name": "string",
      "matched_value": "string",
      "row_data": {}
    }
  ],
  "total_matches": "integer"
}
```

---

## 7. GetDatabaseSize

**Purpose**: Database size analysis

**Parameters**:
- `includeTableBreakdown` (boolean, optional, default: true)
- `topN` (integer, optional, default: 20)

**Response**:
```json
{
  "total_size_mb": "decimal",
  "data_size_mb": "decimal",
  "log_size_mb": "decimal",
  "tables": [
    {
      "table_name": "string",
      "row_count": "integer",
      "data_mb": "decimal",
      "index_mb": "decimal"
    }
  ]
}
```

---

## 8. GenerateModelClass

**Purpose**: Generate code models from schema

**Parameters**:
- `tableName` (string, required)
- `language` (string: 'CSHARP'|'PYTHON'|'TYPESCRIPT')
- `includeValidation` (boolean, optional, default: true)

**Response**:
```json
{
  "generated_code": "string",
  "class_name": "string",
  "properties": []
}
```

---

## Implementation Guidelines

### Standard Response Format
All operations should return:
```json
{
  "server_name": "string",
  "environment": "string",
  "database": "string",
  "operation_type": "string",
  "security_mode": "READ_ONLY",
  "timestamp": "datetime",
  "data": {}
}
```

### Error Handling
```json
{
  "error": "string",
  "error_code": "string",
  "operation_type": "ERROR",
  "help": "string",
  "troubleshooting": []
}
```

### Performance Guidelines
- Default timeout: 30 seconds
- Default row limit: 100-1000 depending on operation
- Cache expensive metadata queries (5-15 min TTL)
- Use LIMITED scan for index stats
- Implement query result pagination

### Security Checks
- Validate all queries are read-only
- Respect user permissions
- Sanitize connection strings in logs
- Rate limit expensive operations
- No exposure of system passwords

---

## API Patterns

### Naming Conventions
- Operations: PascalCase verbs (Get, Search, Generate, Find)
- Parameters: camelCase
- Response fields: snake_case

### Parameter Patterns
- `tableName` + `schemaName` for table operations
- `topN` for limiting results
- `include*` for optional data sections
- `min*` / `max*` for filtering thresholds

### Common Parameters
- `schemaName` (default: 'dbo')
- `maxResults` (default: 100, max: 1000)
- `includeMetadata` (default: true)
- `timeout` (default: 30 seconds)

---

## Implementation Phases

### Phase 1 (High Priority)
1. GetTableRelationships
2. GetIndexInformation
3. GetMissingIndexes
4. GetColumnStatistics

### Phase 2 (Medium Priority)
5. SearchTableData
6. GetQueryExecutionPlan
7. GetDatabaseSize
8. FindColumnsByDataType

### Phase 3 (Low Priority)
9. GenerateModelClass
10. GetIndexFragmentation
11. GetWaitStats
12. GenerateInsertStatements

---

## Testing Requirements

### Unit Tests
- Parameter validation
- SQL query generation
- Response formatting
- Error handling

### Integration Tests
- Actual database operations
- Permission handling
- Timeout behavior
- Large result sets

### Performance Tests
- Query execution time
- Memory usage
- Concurrent operations
- Cache effectiveness

---

## Documentation Requirements

### For Each Operation
- Purpose and use cases
- Parameter descriptions with examples
- Response structure with sample data
- SQL queries used
- Performance considerations
- Security implications

### Code Examples
- C# usage examples
- Common query patterns
- Error handling examples
- Best practices

---

## Success Metrics

### Functionality
- All operations maintain read-only security
- 100% parameter validation coverage
- Comprehensive error messages

### Performance
- Query execution < 5 seconds (p95)
- Cache hit rate > 70%
- Memory usage < 500MB per operation

### Quality
- Code coverage > 80%
- Zero security vulnerabilities
- All public APIs documented

---

**Status**: Ready for implementation
**Next Steps**: Begin Phase 1 implementation
