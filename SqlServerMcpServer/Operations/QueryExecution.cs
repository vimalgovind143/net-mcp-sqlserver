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
        /// Execute a read-only SQL query on the current database with pagination and metadata
        /// </summary>
        /// <param name="query">The SQL query to execute (SELECT statements only)</param>
        /// <param name="maxRows">Maximum rows to return (default 100, max 1000)</param>
        /// <param name="offset">Offset for pagination (default: 0)</param>
        /// <param name="pageSize">Page size for pagination (default: 100, max: 1000)</param>
        /// <param name="includeStatistics">Include query execution statistics (optional, default: false)</param>
        /// <returns>Query results as JSON string</returns>
        [McpServerTool, Description("Execute a read-only SQL query on the current database with pagination and metadata")]
        public static async Task<string> ExecuteQueryAsync(
            [Description("The SQL query to execute (SELECT statements only)")] string query,
            [Description("Maximum rows to return (default 100, max 1000)")] int? maxRows = 100,
            [Description("Offset for pagination (default: 0)")] int? offset = 0,
            [Description("Page size for pagination (default: 100, max: 1000)")] int? pageSize = 100,
            [Description("Include query execution statistics (optional, default: false)")] bool includeStatistics = false)
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

                // Validate read-only operation
                if (!QueryValidator.IsReadOnlyQuery(query, out string blockedOperation))
                {
                    var context = ErrorHelper.CreateBlockedOperationContext(blockedOperation, query);
                    var blockedPayload = ResponseFormatter.CreateBlockedContextResponse(context, sw.ElapsedMilliseconds);
                    return ResponseFormatter.ToJson(blockedPayload);
                }

                // Generate query warnings
                var warnings = QueryValidator.GenerateQueryWarnings(query, effectiveOffset);

                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

                // Enable statistics if requested
                if (includeStatistics)
                {
                    using var statsCommand = new SqlCommand("SET STATISTICS IO ON; SET STATISTICS TIME ON;", connection)
                    {
                        CommandTimeout = SqlConnectionManager.CommandTimeout
                    };
                    await statsCommand.ExecuteNonQueryAsync();
                }

                // Apply pagination and limits
                var finalQuery = QueryFormatter.ApplyPaginationAndLimit(query, limit, effectiveOffset);

                using var command = new SqlCommand(finalQuery, connection)
                {
                    CommandTimeout = SqlConnectionManager.CommandTimeout
                };

                // Execute query and get metadata
                var queryMetadata = new Dictionary<string, object>();
                var columnInfo = new List<Dictionary<string, object>>();

                using var reader = await command.ExecuteReaderAsync();

                // Get column information - only if we have a result set
                if (reader.HasRows)
                {
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var column = new Dictionary<string, object>
                        {
                            ["name"] = reader.GetName(i),
                            ["data_type"] = reader.GetDataTypeName(i),
                            ["sql_type"] = reader.GetFieldType(i).Name,
                            ["is_nullable"] = true, // Default to true for schema info
                            ["max_length"] = 0 // Would need schema query for accurate info
                        };
                        columnInfo.Add(column);
                    }
                }

                // Read results
                var results = new List<Dictionary<string, object>>();
                var rowCount = 0;

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

                var payload = ResponseFormatter.CreateStandardResponse("ExecuteQuery", queryData, sw.ElapsedMilliseconds,
                    warnings: warnings.Any() ? warnings : null, recommendations: recommendations);

                sw.Stop();
                LoggingHelper.LogEnd(corr, "ExecuteQuery", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
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
        /// Execute a read-only SQL query with formatting and parameters (SRS: read_query)
        /// </summary>
        /// <param name="query">T-SQL SELECT statement (read-only)</param>
        /// <param name="timeout">Per-call timeout in seconds (default 30, range 1–300)</param>
        /// <param name="max_rows">Maximum rows to return (default 1000, range 1–10000)</param>
        /// <param name="format">Result format: json | csv | table (HTML)</param>
        /// <param name="parameters">Named parameters to bind (e.g., { id: 42 })</param>
        /// <param name="delimiter">CSV delimiter (default ','. Use 'tab' or \t for tab)</param>
        /// <returns>Query results as JSON string</returns>
        [McpServerTool, Description("Execute a read-only SQL query with formatting and parameters (SRS: read_query)")]
        public static async Task<string> ReadQueryAsync(
            [Description("T-SQL SELECT statement (read-only)")] string query,
            [Description("Per-call timeout in seconds (default 30, range 1–300)")] int? timeout = null,
            [Description("Maximum rows to return (default 1000, range 1–10000)")] int? max_rows = 1000,
            [Description("Result format: json | csv | table (HTML)")] string? format = "json",
            [Description("Named parameters to bind (e.g., { id: 42 })")] Dictionary<string, object>? parameters = null,
            [Description("CSV delimiter (default ','. Use 'tab' or \\t for tab)")] string? delimiter = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("ReadQuery", query);

                // Enforce read-only
                if (!QueryValidator.IsReadOnlyQuery(query, out string blockedOperation))
                {
                    var context = ErrorHelper.CreateBlockedOperationContext(blockedOperation, query);
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
                    return ResponseFormatter.ToJson(
                        ResponseFormatter.CreateErrorContextResponse(validationContext, sw.ElapsedMilliseconds));
                }

                int appliedTimeout = SqlConnectionManager.CommandTimeout;
                if (timeout.HasValue)
                {
                    appliedTimeout = Math.Clamp(timeout.Value, 1, 300);
                }

                int requestedMax = max_rows ?? 1000;
                int appliedMaxRows = Math.Clamp(requestedMax, 1, 10000);

                var finalQuery = QueryFormatter.ApplyTopLimit(query, appliedMaxRows);

                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

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

                var payload = new
                {
                    server_name = SqlConnectionManager.ServerName,
                    environment = SqlConnectionManager.Environment,
                    database = SqlConnectionManager.CurrentDatabase,
                    row_count = results.Count,
                    elapsed_ms = sw.ElapsedMilliseconds,
                    operation_type = "READ_QUERY",
                    security_mode = "READ_ONLY_ENFORCED",
                    format = fmt,
                    columns,
                    applied_limit = appliedMaxRows,
                    result = resultData
                };

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
