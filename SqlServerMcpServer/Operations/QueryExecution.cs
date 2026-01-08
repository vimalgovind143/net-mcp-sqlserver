using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;
using SqlServerMcpServer.Configuration;
using SqlServerMcpServer.Security;
using SqlServerMcpServer.Utilities;
using System.ComponentModel;
using System.Data;

namespace SqlServerMcpServer.Operations
{
    /// <summary>
    /// Handles query execution operations for SELECT statements
    /// </summary>
    public static class QueryExecution
    {
        /// <summary>
        /// Execute a SQL query on the current database with pagination and metadata
        /// </summary>
        /// <param name="query">The SQL query to execute (SELECT, INSERT, UPDATE; DELETE/TRUNCATE require confirmation)</param>
        /// <param name="maxRows">Maximum rows to return (default 100, max 1000)</param>
        /// <param name="offset">Offset for pagination (default: 0)</param>
        /// <param name="pageSize">Page size for pagination (default: 100, max: 1000)</param>
        /// <param name="includeStatistics">Include query execution statistics (optional, default: false)</param>
        /// <param name="confirmUnsafeOperation">Confirm execution of DELETE/TRUNCATE operations (default: false)</param>
        /// <returns>Query results as JSON string</returns>
        [McpServerTool, Description("Execute a SQL query on the current database with pagination and metadata (supports SELECT, INSERT, UPDATE; DELETE/TRUNCATE require confirmation)")]
        public static async Task<string> ExecuteQueryAsync(
            [Description("The SQL query to execute (SELECT, INSERT, UPDATE; DELETE/TRUNCATE require confirmation)")] string query,
            [Description("Maximum rows to return (default 100, max 1000)")] int? maxRows = 100,
            [Description("Offset for pagination (default: 0)")] int? offset = 0,
            [Description("Page size for pagination (default: 100, max: 1000)")] int? pageSize = 100,
            [Description("Include query execution statistics (optional, default: false)")] bool includeStatistics = false,
            [Description("Confirm execution of DELETE/TRUNCATE operations (default: false)")] bool confirmUnsafeOperation = false)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("ExecuteQuery", $"query:{query.Substring(0, Math.Min(query.Length, 100))}...");

                // Validate and normalize parameters
                var effectivePageSize = Math.Min(pageSize ?? 100, 1000);
                var effectiveOffset = offset ?? 0;
                var effectiveMaxRows = Math.Min(maxRows ?? 100, 1000);

                // Use the smaller of maxRows and pageSize
                var limit = Math.Min(effectiveMaxRows, effectivePageSize);

                // Validate query using new DML-aware validation
                if (!QueryValidator.IsDmlQueryAllowed(query, confirmUnsafeOperation, out string? blockedOperation))
                {
                    var requiresConfirmation = QueryValidator.RequiresConfirmation(query);
                    var context = ErrorHelper.CreateBlockedOperationContext(blockedOperation!, query, requiresConfirmation);
                    var blockedPayload = ResponseFormatter.CreateBlockedContextResponse(context, sw.ElapsedMilliseconds);
                    return ResponseFormatter.ToJson(blockedPayload);
                }

                // Generate query warnings
                var warnings = QueryValidator.GenerateQueryWarnings(query, effectiveOffset);

                using var connection = await ConnectionPoolManager.CreateConnectionWithRetryAsync();

                // Enable statistics if requested
                if (includeStatistics)
                {
                    using var statsCommand = new SqlCommand("SET STATISTICS IO ON; SET STATISTICS TIME ON;", connection)
                    {
                        CommandTimeout = SqlConnectionManager.CommandTimeout
                    };
                    await statsCommand.ExecuteNonQueryAsync();
                }

                // Apply pagination and limits (only for SELECT queries)
                var queryType = QueryValidator.ClassifyQuery(query);
                var finalQuery = queryType == QueryType.ReadOnly 
                    ? QueryFormatter.ApplyPaginationAndLimit(query, limit, effectiveOffset) 
                    : query;

                // Execute query and get metadata
                var queryMetadata = new Dictionary<string, object>();
                var columnInfo = new List<Dictionary<string, object>>();
                var results = new List<Dictionary<string, object>>();
                var rowCount = 0;

                // For DML operations, use ExecuteNonQuery directly
                if (queryType != QueryType.ReadOnly)
                {
                    using var dmlCommand = new SqlCommand(finalQuery, connection)
                    {
                        CommandTimeout = SqlConnectionManager.CommandTimeout
                    };
                    rowCount = await dmlCommand.ExecuteNonQueryAsync();

                    queryMetadata["rows_affected"] = rowCount;
                    queryMetadata["columns_returned"] = 0;

                    queryMetadata["execution_time_ms"] = sw.ElapsedMilliseconds;
                    queryMetadata["query_hash"] = query.GetHashCode().ToString();

                    var dmlData = new
                    {
                        query_metadata = queryMetadata,
                        rows_affected = rowCount
                    };

                    var securityMode = queryType switch
                    {
                        QueryType.Delete or QueryType.Truncate => confirmUnsafeOperation ? "DML_WITH_CONFIRMATION" : "READ_ONLY",
                        QueryType.Insert or QueryType.Update => "DML_ALLOWED",
                        _ => "READ_ONLY"
                    };

                    var payload = ResponseFormatter.CreateStandardResponse(
                        "ExecuteQuery",
                        dmlData,
                        sw.ElapsedMilliseconds,
                        warnings: warnings.Any() ? warnings : null,
                        securityMode: securityMode);

                    sw.Stop();
                    LoggingHelper.LogEnd(corr, "ExecuteQuery", true, sw.ElapsedMilliseconds);
                    return ResponseFormatter.ToJson(payload);
                }
                else
                {
                    // For SELECT queries, use ExecuteReader
                    using var selectCommand = new SqlCommand(finalQuery, connection)
                    {
                        CommandTimeout = SqlConnectionManager.CommandTimeout
                    };
                    using var reader = await selectCommand.ExecuteReaderAsync();

                    // Get column information
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var column = new Dictionary<string, object>
                        {
                            ["name"] = reader.GetName(i),
                            ["data_type"] = reader.GetDataTypeName(i),
                            ["sql_type"] = reader.GetFieldType(i).Name,
                            ["is_nullable"] = true,
                            ["max_length"] = 0
                        };
                        columnInfo.Add(column);
                    }

                    // Read results
                    while (await reader.ReadAsync() && rowCount < limit)
                    {
                        var row = new Dictionary<string, object>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            var columnName = reader.GetName(i);
                            var value = reader.GetValue(i);
                            row[columnName] = value is DBNull ? null : value;
                        }
                        results.Add(row);
                        rowCount++;
                    }

                    reader.Close();
                    
                    // Get execution statistics if requested
                    var statistics = new Dictionary<string, object>();
                    if (includeStatistics)
                    {
                        try
                        {
                            // Reset statistics
                            using var resetCommand = new SqlCommand("SET STATISTICS IO OFF; SET STATISTICS TIME OFF;", connection)
                            {
                                CommandTimeout = SqlConnectionManager.CommandTimeout
                            };
                            await resetCommand.ExecuteNonQueryAsync();

                            // Note: In a real implementation, you would capture the statistics output
                            // This is a simplified version
                            statistics["logical_reads"] = "N/A";
                            statistics["physical_reads"] = "N/A";
                            statistics["cpu_time_ms"] = sw.ElapsedMilliseconds;
                            statistics["elapsed_time_ms"] = sw.ElapsedMilliseconds;
                        }
                        catch
                        {
                            statistics["error"] = "Statistics collection failed";
                        }
                    }

                    // Build query metadata
                    queryMetadata["execution_time_ms"] = sw.ElapsedMilliseconds;
                    queryMetadata["rows_affected"] = rowCount;
                    queryMetadata["columns_returned"] = reader.FieldCount;
                    queryMetadata["query_hash"] = query.GetHashCode().ToString();

                    // Calculate pagination info
                    var pagination = new Dictionary<string, object>
                    {
                        ["current_page"] = effectiveOffset / effectivePageSize + 1,
                        ["page_size"] = effectivePageSize,
                        ["offset"] = effectiveOffset,
                        ["has_more"] = rowCount == effectivePageSize
                    };

                    var queryData = new
                    {
                        query_metadata = queryMetadata,
                        columns = columnInfo,
                        pagination = pagination,
                        statistics = includeStatistics ? statistics : null,
                        data = results,
                        applied_limit = limit
                    };

                    var recommendations = new List<string>();
                    if (warnings.Any())
                    {
                        recommendations.Add("Consider optimizing your query for better performance");
                    }

                    var payload = ResponseFormatter.CreateStandardResponse(
                        "ExecuteQuery",
                        queryData,
                        sw.ElapsedMilliseconds,
                        warnings: warnings.Any() ? warnings : null,
                        recommendations: recommendations);

                    sw.Stop();
                    LoggingHelper.LogEnd(corr, "ExecuteQuery", true, sw.ElapsedMilliseconds);
                    return ResponseFormatter.ToJson(payload);
                }
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "ExecuteQuery", query);

                // Add query-specific context
                context.Query = query;

                // Add timeout-specific suggestions
                if (sqlEx.Number == -1 || sqlEx.Number == -2)
                {
                    context.SuggestedFixes.Add("Add WHERE clauses to reduce result set size");
                    context.SuggestedFixes.Add("Use pagination with OFFSET/FETCH clauses");
                    context.SuggestedFixes.Add("Create indexes on columns used in WHERE clauses");
                    context.SuggestedFixes.Add($"Increase SQLSERVER_COMMAND_TIMEOUT environment variable (currently {SqlConnectionManager.CommandTimeout}s)");
                }

                LoggingHelper.LogEnd(Guid.Empty, "ExecuteQuery", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "ExecuteQuery");
                context.Query = query;

                LoggingHelper.LogEnd(Guid.Empty, "ExecuteQuery", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }

        /// <summary>
        /// Execute a SQL query with formatting and parameters (SRS: read_query)
        /// </summary>
        /// <param name="query">T-SQL query (SELECT, INSERT, UPDATE; DELETE/TRUNCATE require confirmation)</param>
        /// <param name="timeout">Per-call timeout in seconds (default 30, range 1–300)</param>
        /// <param name="max_rows">Maximum rows to return (default 1000, range 1–10000)</param>
        /// <param name="format">Result format: json | csv | table (HTML)</param>
        /// <param name="parameters">Named parameters to bind (e.g., { id: 42 })</param>
        /// <param name="delimiter">CSV delimiter (default ','. Use 'tab' or \t for tab)</param>
        /// <param name="confirm_unsafe_operation">Confirm execution of DELETE/TRUNCATE operations (default: false)</param>
        /// <returns>Query results as JSON string</returns>
        [McpServerTool, Description("Execute a SQL query with formatting and parameters (SRS: read_query, supports DML with confirmation for DELETE/TRUNCATE)")]
        public static async Task<string> ReadQueryAsync(
            [Description("T-SQL query (SELECT, INSERT, UPDATE; DELETE/TRUNCATE require confirmation)")] string query,
            [Description("Per-call timeout in seconds (default 30, range 1–300)")] int? timeout = null,
            [Description("Maximum rows to return (default 1000, range 1–10000)")] int? max_rows = 1000,
            [Description("Result format: json | csv | table (HTML)")] string? format = "json",
            [Description("Named parameters to bind (e.g., { id: 42 })")] Dictionary<string, object>? parameters = null,
            [Description("CSV delimiter (default ','. Use 'tab' or \\t for tab)")] string? delimiter = null,
            [Description("Confirm execution of DELETE/TRUNCATE operations (default: false)")] bool confirm_unsafe_operation = false)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("ReadQuery", query);

                // Validate query using new DML-aware validation
                if (!QueryValidator.IsDmlQueryAllowed(query, confirm_unsafe_operation, out string? blockedOperation))
                {
                    var requiresConfirmation = QueryValidator.RequiresConfirmation(query);
                    var context = ErrorHelper.CreateBlockedOperationContext(blockedOperation!, query, requiresConfirmation);
                    return ResponseFormatter.ToJson(
                        ResponseFormatter.CreateBlockedContextResponse(context, sw.ElapsedMilliseconds));
                }

                // Validate and normalize parameters
                var fmt = string.IsNullOrWhiteSpace(format) ? "json" : format.Trim().ToLowerInvariant();
                if (fmt != "json" && fmt != "csv" && fmt != "table")
                {
                    var validationContext = new ErrorContext(
                        ErrorCode.InvalidParameter,
                        "Invalid format. Allowed values: json, csv, table",
                        "ReadQuery"
                    );
                    validationContext.SuggestedFixes.Add("Use format: 'json' (default), 'csv', or 'table'");

                    var validationPayload = new
                    {
                        server_name = SqlConnectionManager.ServerName,
                        environment = SqlConnectionManager.Environment,
                        database = SqlConnectionManager.CurrentDatabase,
                        operation_type = "VALIDATION_ERROR",
                        error = new
                        {
                            code = validationContext.Code.GetDescription(),
                            message = validationContext.Message,
                            suggested_fixes = validationContext.SuggestedFixes
                        }
                    };

                    return ResponseFormatter.ToJson(validationPayload);
                }

                int appliedTimeout = SqlConnectionManager.CommandTimeout;
                if (timeout.HasValue)
                {
                    appliedTimeout = Math.Clamp(timeout.Value, 1, 300);
                }

                int requestedMax = max_rows ?? 1000;
                int appliedMaxRows = Math.Clamp(requestedMax, 1, 10000);

                var queryType = QueryValidator.ClassifyQuery(query);
                var finalQuery = queryType == QueryType.ReadOnly 
                    ? QueryFormatter.ApplyTopLimit(query, appliedMaxRows) 
                    : query;

                using var connection = await ConnectionPoolManager.CreateConnectionWithRetryAsync();

                // Generate warnings for all query types
                var queryWarnings = QueryValidator.GenerateQueryWarnings(query);

                using var command = new SqlCommand(finalQuery, connection)
                {
                    CommandTimeout = appliedTimeout
                };

                // Bind parameters if provided
                if (parameters is not null)
                {
                    foreach (var kvp in parameters)
                    {
                        var name = kvp.Key?.Trim();
                        if (string.IsNullOrEmpty(name)) continue;
                        if (!name.StartsWith("@")) name = "@" + name;
                        var value = kvp.Value ?? DBNull.Value;
                        command.Parameters.AddWithValue(name, value);
                    }
                }

                // For DML operations, use ExecuteNonQuery
                if (queryType != QueryType.ReadOnly)
                {
                    var rowsAffected = await command.ExecuteNonQueryAsync();

                    sw.Stop();
                    LoggingHelper.LogEnd(corr, "ReadQuery", true, sw.ElapsedMilliseconds);

                    var dmlSecurityMode = queryType switch
                    {
                        QueryType.Delete or QueryType.Truncate => confirm_unsafe_operation ? "DML_WITH_CONFIRMATION" : "READ_ONLY",
                        QueryType.Insert or QueryType.Update => "DML_ALLOWED",
                        _ => "READ_ONLY"
                    };

                    var dmlData = new
                    {
                        operation_type = queryType.ToString().ToUpperInvariant(),
                        rows_affected = rowsAffected,
                        format = fmt
                    };

                    var dmlPayload = ResponseFormatter.CreateStandardResponse(
                        "ReadQuery",
                        dmlData,
                        sw.ElapsedMilliseconds,
                        warnings: queryWarnings.Any() ? queryWarnings : null,
                        securityMode: dmlSecurityMode);

                    return ResponseFormatter.ToJson(dmlPayload);
                }

                using var reader = await command.ExecuteReaderAsync();

                // Column metadata
                var columns = new List<Dictionary<string, object>>();
                var columnNames = new List<string>();
                try
                {
                    var schema = reader.GetSchemaTable();
                    if (schema is not null)
                    {
                        foreach (DataRow row in schema.Rows)
                        {
                            var name = row["ColumnName"]?.ToString() ?? string.Empty;
                            var type = (row["DataType"] as Type)?.FullName ?? (row["DataType"]?.ToString() ?? "");
                            var allowNull = row.Table.Columns.Contains("AllowDBNull") ? (row["AllowDBNull"] as bool? ?? false) : false;
                            var size = row.Table.Columns.Contains("ColumnSize") ? (row["ColumnSize"] as int? ?? 0) : 0;
                            columnNames.Add(name);
                            columns.Add(new Dictionary<string, object>
                            {
                                ["name"] = name,
                                ["data_type"] = type,
                                ["allow_null"] = allowNull,
                                ["size"] = size
                            });
                        }
                    }
                    else
                    {
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            var name = reader.GetName(i);
                            columnNames.Add(name);
                            columns.Add(new Dictionary<string, object>
                            {
                                ["name"] = name,
                                ["data_type"] = reader.GetFieldType(i).FullName ?? reader.GetFieldType(i).Name,
                                ["allow_null"] = true,
                                ["size"] = 0
                            });
                        }
                    }
                }
                catch
                {
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var name = reader.GetName(i);
                        columnNames.Add(name);
                        columns.Add(new Dictionary<string, object>
                        {
                            ["name"] = name,
                            ["data_type"] = reader.GetFieldType(i).FullName ?? reader.GetFieldType(i).Name,
                            ["allow_null"] = true,
                            ["size"] = 0
                        });
                    }
                }

                // Read results
                var results = new List<Dictionary<string, object>>();
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var colName = reader.GetName(i);
                        var value = reader.GetValue(i);
                        row[colName] = value is DBNull ? null : value;
                    }
                    results.Add(row);
                }

                string? rendered = null;
                char delim = DataFormatter.ParseDelimiter(delimiter);
                if (fmt == "csv")
                {
                    rendered = DataFormatter.ToCsv(results, columnNames, delim);
                }
                else if (fmt == "table")
                {
                    rendered = DataFormatter.ToHtmlTable(results, columnNames);
                }

                sw.Stop();
                LoggingHelper.LogEnd(corr, "ReadQuery", true, sw.ElapsedMilliseconds);

                // Ensure consistent typing for conditional result selection
                object resultData = fmt == "json" ? (object)results : (object)(rendered ?? string.Empty);

                var readData = new
                {
                    row_count = results.Count,
                    format = fmt,
                    columns,
                    applied_limit = appliedMaxRows,
                    result = resultData
                };

                var payload = ResponseFormatter.CreateStandardResponse(
                    "ReadQuery",
                    readData,
                    sw.ElapsedMilliseconds,
                    warnings: queryWarnings.Any() ? queryWarnings : null);

                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "ReadQuery", query);

                // Add query-specific context
                if (sqlEx.Number == -1 || sqlEx.Number == -2)
                {
                    context.SuggestedFixes.Add("Reduce the number of rows returned using WHERE clauses");
                    context.SuggestedFixes.Add("Use pagination with OFFSET and FETCH clauses");
                    context.SuggestedFixes.Add($"Increase timeout parameter (currently {timeout ?? SqlConnectionManager.CommandTimeout} seconds)");
                }

                LoggingHelper.LogEnd(Guid.Empty, "ReadQuery", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "ReadQuery");
                context.Query = query;

                LoggingHelper.LogEnd(Guid.Empty, "ReadQuery", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }
    }
}
