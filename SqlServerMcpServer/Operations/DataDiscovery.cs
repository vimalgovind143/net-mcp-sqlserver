using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;
using SqlServerMcpServer.Configuration;
using SqlServerMcpServer.Security;
using SqlServerMcpServer.Utilities;
using System.ComponentModel;

namespace SqlServerMcpServer.Operations
{
    /// <summary>
    /// Handles data discovery operations for searching, statistics, and column analysis
    /// </summary>
    public static class DataDiscovery
    {
        /// <summary>
        /// Search for data across tables with pattern matching
        /// </summary>
        /// <param name="searchPattern">Search pattern (supports LIKE wildcards)</param>
        /// <param name="tableName">Table name to search (optional)</param>
        /// <param name="schemaName">Schema name (default: 'dbo')</param>
        /// <param name="columnNames">Specific column names to search (optional, comma-separated)</param>
        /// <param name="maxRows">Maximum rows to return per table (default: 10)</param>
        /// <param name="maxTables">Maximum tables to search (default: 10)</param>
        /// <returns>Search results as JSON string</returns>
        [McpServerTool, Description("Search for data across tables with pattern matching")]
        public static async Task<string> SearchTableData(
            [Description("Search pattern (supports LIKE wildcards)")] string searchPattern,
            [Description("Table name to search (optional)")] string? tableName = null,
            [Description("Schema name (default: 'dbo')")] string? schemaName = "dbo",
            [Description("Specific column names to search (optional, comma-separated)")] string? columnNames = null,
            [Description("Maximum rows to return per table (default: 10)")] int maxRows = 10,
            [Description("Maximum tables to search (default: 10)")] int maxTables = 10)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("SearchTableData", $"pattern:{searchPattern}, table:{tableName}, maxRows:{maxRows}");
                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

                // Get searchable tables and columns
                var tableQuery = @"
                    SELECT TOP (@MaxTables)
                        t.name AS table_name,
                        s.name AS schema_name,
                        c.name AS column_name,
                        ty.name AS data_type,
                        c.max_length
                    FROM sys.tables t
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    INNER JOIN sys.columns c ON t.object_id = c.object_id
                    INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
                    WHERE (@TableName IS NULL OR t.name = @TableName)
                    AND (@SchemaName IS NULL OR s.name = @SchemaName)
                    AND (@ColumnNames IS NULL OR c.name IN (SELECT value FROM STRING_SPLIT(@ColumnNames, ',')))
                    AND ty.name IN ('varchar', 'nvarchar', 'char', 'nchar', 'text', 'ntext', 'xml')
                    AND t.is_ms_shipped = 0
                    ORDER BY t.name, c.name";

                using var tableCommand = new SqlCommand(tableQuery, connection);
                tableCommand.Parameters.AddWithValue("@TableName", (object?)tableName ?? DBNull.Value);
                tableCommand.Parameters.AddWithValue("@SchemaName", (object?)schemaName ?? DBNull.Value);
                tableCommand.Parameters.AddWithValue("@ColumnNames", (object?)columnNames ?? DBNull.Value);
                tableCommand.Parameters.AddWithValue("@MaxTables", maxTables);

                using var tableReader = await tableCommand.ExecuteReaderAsync();
                
                var searchResults = new List<Dictionary<string, object>>();
                var totalMatches = 0;

                while (await tableReader.ReadAsync())
                {
                    var currentTableName = tableReader["table_name"].ToString()!;
                    var currentSchemaName = tableReader["schema_name"].ToString()!;
                    var currentColumnName = tableReader["column_name"].ToString()!;
                    var dataType = tableReader["data_type"].ToString()!;

                    // Build dynamic search query for each column
                    var searchQuery = $@"
                        SELECT TOP (@MaxRows)
                            '{currentTableName}' AS table_name,
                            '{currentSchemaName}' AS schema_name,
                            '{currentColumnName}' AS column_name,
                            CAST([{currentColumnName}] AS NVARCHAR(MAX)) AS matched_value,
                            ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS row_number
                        FROM [{currentSchemaName}].[{currentTableName}]
                        WHERE CAST([{currentColumnName}] AS NVARCHAR(MAX)) LIKE @SearchPattern
                        AND CAST([{currentColumnName}] AS NVARCHAR(MAX)) IS NOT NULL
                        AND LEN(CAST([{currentColumnName}] AS NVARCHAR(MAX))) > 0";

                    try
                    {
                        using var searchCommand = new SqlCommand(searchQuery, connection);
                        searchCommand.Parameters.AddWithValue("@SearchPattern", searchPattern);
                        searchCommand.Parameters.AddWithValue("@MaxRows", maxRows);

                        using var searchReader = await searchCommand.ExecuteReaderAsync();
                        var tableMatches = new List<Dictionary<string, object>>();

                        while (await searchReader.ReadAsync())
                        {
                            var match = new Dictionary<string, object>
                            {
                                ["table_name"] = searchReader["table_name"].ToString()!,
                                ["schema_name"] = searchReader["schema_name"].ToString()!,
                                ["column_name"] = searchReader["column_name"].ToString()!,
                                ["matched_value"] = searchReader["matched_value"].ToString()!,
                                ["row_number"] = Convert.ToInt32(searchReader["row_number"]),
                                ["data_type"] = dataType
                            };
                            tableMatches.Add(match);
                            totalMatches++;
                        }

                        if (tableMatches.Any())
                        {
                            searchResults.Add(new Dictionary<string, object>
                            {
                                ["table_name"] = currentTableName,
                                ["schema_name"] = currentSchemaName,
                                ["column_name"] = currentColumnName,
                                ["data_type"] = dataType,
                                ["match_count"] = tableMatches.Count,
                                ["matches"] = tableMatches
                            });
                        }
                    }
                    catch (Exception searchEx)
                    {
                        // Log but continue with other tables
                        LoggingHelper.LogEnd(Guid.Empty, $"SearchTableData-{currentTableName}", false, sw.ElapsedMilliseconds, searchEx.Message);
                    }
                }

                var payload = new
                {
                    server_name = SqlConnectionManager.ServerName,
                    environment = SqlConnectionManager.Environment,
                    database = SqlConnectionManager.CurrentDatabase,
                    search_results = searchResults,
                    summary = new
                    {
                        search_pattern = searchPattern,
                        tables_searched = searchResults.Count,
                        total_matches = totalMatches,
                        max_rows_per_table = maxRows,
                        max_tables_searched = maxTables
                    },
                    filters_applied = new
                    {
                        table_name = tableName,
                        schema_name = schemaName,
                        column_names = columnNames?.Split(",", StringSplitOptions.RemoveEmptyEntries)
                    }
                };
                sw.Stop();
                LoggingHelper.LogEnd(corr, "SearchTableData", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "SearchTableData");
                LoggingHelper.LogEnd(Guid.Empty, "SearchTableData", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "SearchTableData");
                LoggingHelper.LogEnd(Guid.Empty, "SearchTableData", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }

        /// <summary>
        /// Get column statistics and data distribution
        /// </summary>
        /// <param name="tableName">Table name (required)</param>
        /// <param name="schemaName">Schema name (default: 'dbo')</param>
        /// <param name="columnName">Column name (optional - if not provided, returns all columns)</param>
        /// <param name="includeHistogram">Include histogram data (default: false)</param>
        /// <param name="sampleSize">Sample size for statistics (default: 10000)</param>
        /// <returns>Column statistics as JSON string</returns>
        [McpServerTool, Description("Get column statistics and data distribution")]
        public static async Task<string> GetColumnStatistics(
            [Description("Table name (required)")] string tableName,
            [Description("Schema name (default: 'dbo')")] string? schemaName = "dbo",
            [Description("Column name (optional - if not provided, returns all columns)")] string? columnName = null,
            [Description("Include histogram data (default: false)")] bool includeHistogram = false,
            [Description("Sample size for statistics (default: 10000)")] int sampleSize = 10000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("GetColumnStatistics", $"table:{tableName}, column:{columnName}, sample:{sampleSize}");
                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

                // Get column information
                var columnQuery = @"
                    SELECT 
                        c.name AS column_name,
                        t.name AS data_type,
                        c.max_length,
                        c.precision,
                        c.scale,
                        c.is_nullable,
                        c.is_identity,
                        c.is_computed,
                        cc.definition AS computed_definition,
                        dc.definition AS default_constraint,
                        ep.value AS description
                    FROM sys.columns c
                    INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
                    INNER JOIN sys.tables tbl ON c.object_id = tbl.object_id
                    INNER JOIN sys.schemas s ON tbl.schema_id = s.schema_id
                    LEFT JOIN sys.computed_columns cc ON c.object_id = cc.object_id AND c.column_id = cc.column_id
                    LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
                    LEFT JOIN sys.extended_properties ep ON c.object_id = ep.major_id AND c.column_id = ep.minor_id AND ep.name = 'MS_Description'
                    WHERE tbl.name = @TableName
                    AND s.name = @SchemaName
                    AND (@ColumnName IS NULL OR c.name = @ColumnName)
                    ORDER BY c.column_id";

                using var columnCommand = new SqlCommand(columnQuery, connection);
                columnCommand.Parameters.AddWithValue("@TableName", tableName);
                columnCommand.Parameters.AddWithValue("@SchemaName", schemaName ?? "dbo");
                columnCommand.Parameters.AddWithValue("@ColumnName", (object?)columnName ?? DBNull.Value);

                using var columnReader = await columnCommand.ExecuteReaderAsync();
                
                var columnStats = new List<Dictionary<string, object>>();

                while (await columnReader.ReadAsync())
                {
                    var currentColumnName = columnReader["column_name"].ToString()!;
                    var dataType = columnReader["data_type"].ToString()!;
                    var isNullable = Convert.ToBoolean(columnReader["is_nullable"]);
                    var isIdentity = Convert.ToBoolean(columnReader["is_identity"]);
                    var isComputed = Convert.ToBoolean(columnReader["is_computed"]);

                    var stats = new Dictionary<string, object>
                    {
                        ["column_name"] = currentColumnName,
                        ["data_type"] = dataType,
                        ["max_length"] = Convert.ToInt32(columnReader["max_length"]),
                        ["precision"] = Convert.ToInt32(columnReader["precision"]),
                        ["scale"] = Convert.ToInt32(columnReader["scale"]),
                        ["is_nullable"] = isNullable,
                        ["is_identity"] = isIdentity,
                        ["is_computed"] = isComputed,
                        ["computed_definition"] = columnReader["computed_definition"]?.ToString(),
                        ["default_constraint"] = columnReader["default_constraint"]?.ToString(),
                        ["description"] = columnReader["description"]?.ToString()
                    };

                    // Skip computed columns for detailed statistics
                    if (!isComputed)
                    {
                        try
                        {
                            // Get basic statistics
                            var statsQuery = $@"
                                SELECT 
                                    COUNT(*) AS total_rows,
                                    COUNT(DISTINCT [{currentColumnName}]) AS distinct_count,
                                    SUM(CASE WHEN [{currentColumnName}] IS NULL THEN 1 ELSE 0 END) AS null_count,
                                    CASE 
                                        WHEN COUNT(*) > 0 THEN CAST(SUM(CASE WHEN [{currentColumnName}] IS NULL THEN 1 ELSE 0 END) * 100.0 / COUNT(*) AS DECIMAL(10,2))
                                        ELSE 0
                                    END AS null_percentage
                                FROM [{schemaName}].[{tableName}]
                                WHERE 1=1";

                            // Add sample condition for large tables
                            if (sampleSize > 0)
                            {
                                statsQuery = $@"
                                    SELECT 
                                        COUNT(*) AS total_rows,
                                        COUNT(DISTINCT [{currentColumnName}]) AS distinct_count,
                                        SUM(CASE WHEN [{currentColumnName}] IS NULL THEN 1 ELSE 0 END) AS null_count,
                                        CASE 
                                            WHEN COUNT(*) > 0 THEN CAST(SUM(CASE WHEN [{currentColumnName}] IS NULL THEN 1 ELSE 0 END) * 100.0 / COUNT(*) AS DECIMAL(10,2))
                                            ELSE 0
                                        END AS null_percentage
                                    FROM (
                                        SELECT TOP (@SampleSize) [{currentColumnName}]
                                        FROM [{schemaName}].[{tableName}]
                                        WHERE [{currentColumnName}] IS NOT NULL
                                    ) AS sample_data";
                            }

                            using var statsCommand = new SqlCommand(statsQuery, connection);
                            if (sampleSize > 0)
                            {
                                statsCommand.Parameters.AddWithValue("@SampleSize", sampleSize);
                            }

                            using var statsReader = await statsCommand.ExecuteReaderAsync();
                            if (await statsReader.ReadAsync())
                            {
                                stats["total_rows"] = Convert.ToInt64(statsReader["total_rows"]);
                                stats["distinct_count"] = Convert.ToInt64(statsReader["distinct_count"]);
                                stats["null_count"] = Convert.ToInt64(statsReader["null_count"]);
                                stats["null_percentage"] = Convert.ToDecimal(statsReader["null_percentage"]);
                            }

                            // Get min/max for numeric and date types
                            if (dataType.Contains("int") || dataType.Contains("decimal") || dataType.Contains("numeric") || 
                                dataType.Contains("float") || dataType.Contains("real") || dataType.Contains("money") ||
                                dataType.Contains("datetime") || dataType.Contains("date") || dataType.Contains("time"))
                            {
                                try
                                {
                                    var rangeQuery = $@"
                                        SELECT 
                                            MIN(CAST([{currentColumnName}] AS SQL_VARIANT)) AS min_value,
                                            MAX(CAST([{currentColumnName}] AS SQL_VARIANT)) AS max_value
                                        FROM [{schemaName}].[{tableName}]
                                        WHERE [{currentColumnName}] IS NOT NULL";

                                    using var rangeCommand = new SqlCommand(rangeQuery, connection);
                                    using var rangeReader = await rangeCommand.ExecuteReaderAsync();
                                    if (await rangeReader.ReadAsync())
                                    {
                                        stats["min_value"] = rangeReader["min_value"]?.ToString();
                                        stats["max_value"] = rangeReader["max_value"]?.ToString();
                                    }
                                }
                                catch
                                {
                                    // Skip range stats if conversion fails
                                }
                            }

                            // Get top values for string columns
                            if (dataType.Contains("varchar") || dataType.Contains("nvarchar") || 
                                dataType.Contains("char") || dataType.Contains("nchar"))
                            {
                                try
                                {
                                    var topValuesQuery = $@"
                                        SELECT TOP 10
                                            [{currentColumnName}] AS value,
                                            COUNT(*) AS frequency
                                        FROM [{schemaName}].[{tableName}]
                                        WHERE [{currentColumnName}] IS NOT NULL
                                        AND LEN([{currentColumnName}]) > 0
                                        GROUP BY [{currentColumnName}]
                                        ORDER BY COUNT(*) DESC, [{currentColumnName}]";

                                    using var topValuesCommand = new SqlCommand(topValuesQuery, connection);
                                    using var topValuesReader = await topValuesCommand.ExecuteReaderAsync();
                                    
                                    var topValues = new List<Dictionary<string, object>>();
                                    while (await topValuesReader.ReadAsync())
                                    {
                                        topValues.Add(new Dictionary<string, object>
                                        {
                                            ["value"] = topValuesReader["value"].ToString()!,
                                            ["frequency"] = Convert.ToInt64(topValuesReader["frequency"])
                                        });
                                    }
                                    stats["top_values"] = topValues;
                                }
                                catch
                                {
                                    // Skip top values if query fails
                                }
                            }
                        }
                        catch (Exception statsEx)
                        {
                            stats["statistics_error"] = statsEx.Message;
                        }
                    }

                    columnStats.Add(stats);
                }

                var payload = new
                {
                    server_name = SqlConnectionManager.ServerName,
                    environment = SqlConnectionManager.Environment,
                    database = SqlConnectionManager.CurrentDatabase,
                    table_name = tableName,
                    schema_name = schemaName,
                    column_statistics = columnStats,
                    parameters = new
                    {
                        column_name = columnName,
                        include_histogram = includeHistogram,
                        sample_size = sampleSize
                    }
                };
                sw.Stop();
                LoggingHelper.LogEnd(corr, "GetColumnStatistics", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "GetColumnStatistics");
                LoggingHelper.LogEnd(Guid.Empty, "GetColumnStatistics", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "GetColumnStatistics");
                LoggingHelper.LogEnd(Guid.Empty, "GetColumnStatistics", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }

        /// <summary>
        /// Find columns by data type across all tables
        /// </summary>
        /// <param name="dataType">Data type to search for (e.g., 'varchar', 'int', 'datetime')</param>
        /// <param name="schemaName">Schema name filter (optional)</param>
        /// <param name="tableName">Table name filter (optional)</param>
        /// <param name="includeNullable">Include nullable columns (default: true)</param>
        /// <param name="includeNotNullable">Include non-nullable columns (default: true)</param>
        /// <param name="includeIdentity">Include identity columns (default: true)</param>
        /// <param name="includeComputed">Include computed columns (default: false)</param>
        /// <returns>Columns by data type as JSON string</returns>
        [McpServerTool, Description("Find columns by data type across all tables")]
        public static async Task<string> FindColumnsByDataType(
            [Description("Data type to search for (e.g., 'varchar', 'int', 'datetime')")] string dataType,
            [Description("Schema name filter (optional)")] string? schemaName = null,
            [Description("Table name filter (optional)")] string? tableName = null,
            [Description("Include nullable columns (default: true)")] bool includeNullable = true,
            [Description("Include non-nullable columns (default: true)")] bool includeNotNullable = true,
            [Description("Include identity columns (default: true)")] bool includeIdentity = true,
            [Description("Include computed columns (default: false)")] bool includeComputed = false)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("FindColumnsByDataType", $"type:{dataType}, schema:{schemaName}, table:{tableName}");
                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

                var query = @"
                    SELECT 
                        s.name AS schema_name,
                        t.name AS table_name,
                        c.name AS column_name,
                        ty.name AS data_type,
                        c.max_length,
                        c.precision,
                        c.scale,
                        c.is_nullable,
                        c.is_identity,
                        c.is_computed,
                        cc.definition AS computed_definition,
                        dc.definition AS default_constraint,
                        ep.value AS description,
                        OBJECT_DEFINITION(c.default_object_id) AS default_definition,
                        CASE 
                            WHEN pk.column_id IS NOT NULL THEN 1
                            ELSE 0
                        END AS is_primary_key,
                        CASE 
                            WHEN fk.column_id IS NOT NULL THEN 1
                            ELSE 0
                        END AS is_foreign_key,
                        CASE 
                            WHEN ix.column_id IS NOT NULL THEN 1
                            ELSE 0
                        END AS is_indexed,
                        p.rows AS table_row_count,
                        CONVERT(DECIMAL(10,2), SUM(a.total_pages) * 8.0 / 1024.0) AS table_size_mb
                    FROM sys.columns c
                    INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
                    INNER JOIN sys.tables t ON c.object_id = t.object_id
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    LEFT JOIN sys.computed_columns cc ON c.object_id = cc.object_id AND c.column_id = cc.column_id
                    LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
                    LEFT JOIN sys.extended_properties ep ON c.object_id = ep.major_id AND c.column_id = ep.minor_id AND ep.name = 'MS_Description'
                    LEFT JOIN sys.index_columns pk ON c.object_id = pk.object_id AND c.column_id = pk.column_id AND pk.index_id IN (
                        SELECT index_id FROM sys.indexes WHERE object_id = c.object_id AND is_primary_key = 1
                    )
                    LEFT JOIN sys.index_columns fk ON c.object_id = fk.object_id AND c.column_id = fk.column_id AND fk.index_id IN (
                        SELECT index_id FROM sys.indexes WHERE object_id = c.object_id AND is_primary_key = 0
                    )
                    LEFT JOIN sys.index_columns ix ON c.object_id = ix.object_id AND c.column_id = ix.column_id
                    LEFT JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0,1)
                    LEFT JOIN sys.allocation_units a ON p.partition_id = a.container_id
                    WHERE (ty.name LIKE @DataType OR @DataType = 'ALL')
                    AND (@SchemaName IS NULL OR s.name = @SchemaName)
                    AND (@TableName IS NULL OR t.name = @TableName)
                    AND (@IncludeNullable = 1 OR c.is_nullable = 0)
                    AND (@IncludeNotNullable = 1 OR c.is_nullable = 1)
                    AND (@IncludeIdentity = 1 OR c.is_identity = 0)
                    AND (@IncludeComputed = 1 OR c.is_computed = 0)
                    AND t.is_ms_shipped = 0
                    GROUP BY s.name, t.name, c.name, ty.name, c.max_length, c.precision, c.scale, 
                             c.is_nullable, c.is_identity, c.is_computed, cc.definition, dc.definition, ep.value,
                             c.default_object_id, pk.column_id, fk.column_id, ix.column_id, p.rows
                    ORDER BY s.name, t.name, c.column_id";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@DataType", dataType.Contains("%") ? dataType : $"%{dataType}%");
                command.Parameters.AddWithValue("@SchemaName", (object?)schemaName ?? DBNull.Value);
                command.Parameters.AddWithValue("@TableName", (object?)tableName ?? DBNull.Value);
                command.Parameters.AddWithValue("@IncludeNullable", includeNullable);
                command.Parameters.AddWithValue("@IncludeNotNullable", includeNotNullable);
                command.Parameters.AddWithValue("@IncludeIdentity", includeIdentity);
                command.Parameters.AddWithValue("@IncludeComputed", includeComputed);

                using var reader = await command.ExecuteReaderAsync();
                
                var columns = new List<Dictionary<string, object>>();
                var summary = new Dictionary<string, object>
                {
                    ["total_columns"] = 0,
                    ["total_tables"] = new HashSet<string>(),
                    ["data_type_distribution"] = new Dictionary<string, int>(),
                    ["schema_distribution"] = new Dictionary<string, int>()
                };

                while (await reader.ReadAsync())
                {
                    var column = new Dictionary<string, object>
                    {
                        ["schema_name"] = reader["schema_name"].ToString()!,
                        ["table_name"] = reader["table_name"].ToString()!,
                        ["column_name"] = reader["column_name"].ToString()!,
                        ["data_type"] = reader["data_type"].ToString()!,
                        ["max_length"] = Convert.ToInt32(reader["max_length"]),
                        ["precision"] = Convert.ToInt32(reader["precision"]),
                        ["scale"] = Convert.ToInt32(reader["scale"]),
                        ["is_nullable"] = Convert.ToBoolean(reader["is_nullable"]),
                        ["is_identity"] = Convert.ToBoolean(reader["is_identity"]),
                        ["is_computed"] = Convert.ToBoolean(reader["is_computed"]),
                        ["computed_definition"] = reader["computed_definition"]?.ToString(),
                        ["default_constraint"] = reader["default_constraint"]?.ToString(),
                        ["description"] = reader["description"]?.ToString(),
                        ["is_primary_key"] = Convert.ToBoolean(reader["is_primary_key"]),
                        ["is_foreign_key"] = Convert.ToBoolean(reader["is_foreign_key"]),
                        ["is_indexed"] = Convert.ToBoolean(reader["is_indexed"]),
                        ["table_row_count"] = Convert.ToInt64(reader["table_row_count"]),
                        ["table_size_mb"] = Convert.ToDecimal(reader["table_size_mb"])
                    };

                    // Add formatted data type info
                    var dataTypeName = reader["data_type"].ToString()!;
                    var maxLength = Convert.ToInt32(reader["max_length"]);
                    var precision = Convert.ToInt32(reader["precision"]);
                    var scale = Convert.ToInt32(reader["scale"]);

                    string formattedDataType;
                    if (dataTypeName.Contains("char") || dataTypeName.Contains("binary"))
                    {
                        formattedDataType = maxLength == -1 ? $"{dataTypeName}(MAX)" : $"{dataTypeName}({maxLength})";
                    }
                    else if (dataTypeName.Contains("decimal") || dataTypeName.Contains("numeric"))
                    {
                        formattedDataType = $"{dataTypeName}({precision},{scale})";
                    }
                    else
                    {
                        formattedDataType = dataTypeName;
                    }
                    column["formatted_data_type"] = formattedDataType;

                    columns.Add(column);

                    // Update summary
                    summary["total_columns"] = Convert.ToInt32(summary["total_columns"]) + 1;
                    ((HashSet<string>)summary["total_tables"]).Add($"{reader["schema_name"]}.{reader["table_name"]}");

                    var dataTypeDict = (Dictionary<string, int>)summary["data_type_distribution"];
                    if (!dataTypeDict.ContainsKey(dataTypeName))
                        dataTypeDict[dataTypeName] = 0;
                    dataTypeDict[dataTypeName]++;

                    var schemaDict = (Dictionary<string, int>)summary["schema_distribution"];
                    var schemaNameValue = reader["schema_name"].ToString()!;
                    if (!schemaDict.ContainsKey(schemaNameValue))
                        schemaDict[schemaNameValue] = 0;
                    schemaDict[schemaNameValue]++;
                }

                summary["total_tables"] = ((HashSet<string>)summary["total_tables"]).Count;

                var payload = new
                {
                    server_name = SqlConnectionManager.ServerName,
                    environment = SqlConnectionManager.Environment,
                    database = SqlConnectionManager.CurrentDatabase,
                    columns = columns,
                    summary = summary,
                    filters_applied = new
                    {
                        data_type = dataType,
                        schema_name = schemaName,
                        table_name = tableName,
                        include_nullable = includeNullable,
                        include_not_nullable = includeNotNullable,
                        include_identity = includeIdentity,
                        include_computed = includeComputed
                    }
                };
                sw.Stop();
                LoggingHelper.LogEnd(corr, "FindColumnsByDataType", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "FindColumnsByDataType");
                LoggingHelper.LogEnd(Guid.Empty, "FindColumnsByDataType", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "FindColumnsByDataType");
                LoggingHelper.LogEnd(Guid.Empty, "FindColumnsByDataType", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }

        /// <summary>
        /// Find tables containing specific column names
        /// </summary>
        /// <param name="columnName">Column name to search for (supports LIKE wildcards)</param>
        /// <param name="schemaName">Schema name filter (optional)</param>
        /// <param name="exactMatch">Exact column name match (default: false)</param>
        /// <param name="includeSystemTables">Include system tables (default: false)</param>
        /// <param name="includeRowCount">Include row count for each table (default: true)</param>
        /// <param name="maxResults">Maximum number of results to return (default: 100)</param>
        /// <returns>Tables with column as JSON string</returns>
        [McpServerTool, Description("Find tables containing specific column names")]
        public static async Task<string> FindTablesWithColumn(
            [Description("Column name to search for (supports LIKE wildcards)")] string columnName,
            [Description("Schema name filter (optional)")] string? schemaName = null,
            [Description("Exact column name match (default: false)")] bool exactMatch = false,
            [Description("Include system tables (default: false)")] bool includeSystemTables = false,
            [Description("Include row count for each table (default: true)")] bool includeRowCount = true,
            [Description("Maximum number of results to return (default: 100)")] int maxResults = 100)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("FindTablesWithColumn", $"column:{columnName}, schema:{schemaName}, exact:{exactMatch}");
                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

                var query = @"
                    SELECT TOP (@MaxResults)
                        s.name AS schema_name,
                        t.name AS table_name,
                        c.name AS column_name,
                        ty.name AS data_type,
                        c.max_length,
                        c.precision,
                        c.scale,
                        c.is_nullable,
                        c.is_identity,
                        c.is_computed,
                        cc.definition AS computed_definition,
                        dc.definition AS default_constraint,
                        ep.value AS description,
                        CASE 
                            WHEN pk.column_id IS NOT NULL THEN 1
                            ELSE 0
                        END AS is_primary_key,
                        CASE 
                            WHEN fk.column_id IS NOT NULL THEN 1
                            ELSE 0
                        END AS is_foreign_key,
                        CASE 
                            WHEN ix.column_id IS NOT NULL THEN 1
                            ELSE 0
                        END AS is_indexed,
                        t.create_date,
                        t.modify_date,
                        t.is_memory_optimized,
                        t.temporal_type_desc
                    FROM sys.columns c
                    INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
                    INNER JOIN sys.tables t ON c.object_id = t.object_id
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    LEFT JOIN sys.computed_columns cc ON c.object_id = cc.object_id AND c.column_id = cc.column_id
                    LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
                    LEFT JOIN sys.extended_properties ep ON c.object_id = ep.major_id AND c.column_id = ep.minor_id AND ep.name = 'MS_Description'
                    LEFT JOIN sys.index_columns pk ON c.object_id = pk.object_id AND c.column_id = pk.column_id AND pk.index_id IN (
                        SELECT index_id FROM sys.indexes WHERE object_id = c.object_id AND is_primary_key = 1
                    )
                    LEFT JOIN sys.index_columns fk ON c.object_id = fk.object_id AND c.column_id = fk.column_id AND fk.index_id IN (
                        SELECT index_id FROM sys.indexes WHERE object_id = c.object_id AND is_primary_key = 0
                    )
                    LEFT JOIN sys.index_columns ix ON c.object_id = ix.object_id AND c.column_id = ix.column_id
                    WHERE (@ExactMatch = 1 AND c.name = @ColumnName) OR (@ExactMatch = 0 AND c.name LIKE @ColumnName)
                    AND (@SchemaName IS NULL OR s.name = @SchemaName)
                    AND (@IncludeSystemTables = 1 OR t.is_ms_shipped = 0)
                    ORDER BY s.name, t.name, c.column_id";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@ColumnName", exactMatch ? columnName : $"%{columnName}%");
                command.Parameters.AddWithValue("@SchemaName", (object?)schemaName ?? DBNull.Value);
                command.Parameters.AddWithValue("@ExactMatch", exactMatch);
                command.Parameters.AddWithValue("@IncludeSystemTables", includeSystemTables);
                command.Parameters.AddWithValue("@MaxResults", maxResults);

                using var reader = await command.ExecuteReaderAsync();
                
                var tables = new Dictionary<string, Dictionary<string, object>>();
                var summary = new Dictionary<string, object>
                {
                    ["total_tables"] = 0,
                    ["total_columns"] = 0,
                    ["schema_distribution"] = new Dictionary<string, int>(),
                    ["data_type_distribution"] = new Dictionary<string, int>()
                };

                while (await reader.ReadAsync())
                {
                    var schemaNameValue = reader["schema_name"].ToString()!;
                    var tableNameValue = reader["table_name"].ToString()!;
                    var tableKey = $"{schemaNameValue}.{tableNameValue}";

                    if (!tables.ContainsKey(tableKey))
                    {
                        tables[tableKey] = new Dictionary<string, object>
                        {
                            ["schema_name"] = schemaNameValue,
                            ["table_name"] = tableNameValue,
                            ["columns"] = new List<Dictionary<string, object>>(),
                            ["create_date"] = Convert.ToDateTime(reader["create_date"]),
                            ["modify_date"] = Convert.ToDateTime(reader["modify_date"]),
                            ["is_memory_optimized"] = Convert.ToBoolean(reader["is_memory_optimized"]),
                            ["temporal_type_desc"] = reader["temporal_type_desc"].ToString()!
                        };

                        // Get row count if requested
                        if (includeRowCount)
                        {
                            try
                            {
                                var rowCountQuery = @"
                                    SELECT COUNT(*) AS row_count
                                    FROM [" + schemaNameValue + "].[" + tableNameValue + "]";

                                using var rowCountCommand = new SqlCommand(rowCountQuery, connection);
                                var rowCount = await rowCountCommand.ExecuteScalarAsync();
                                tables[tableKey]["row_count"] = Convert.ToInt64(rowCount);
                            }
                            catch
                            {
                                tables[tableKey]["row_count"] = -1; // Unable to get row count
                            }
                        }
                    }

                    var column = new Dictionary<string, object>
                    {
                        ["column_name"] = reader["column_name"].ToString()!,
                        ["data_type"] = reader["data_type"].ToString()!,
                        ["max_length"] = Convert.ToInt32(reader["max_length"]),
                        ["precision"] = Convert.ToInt32(reader["precision"]),
                        ["scale"] = Convert.ToInt32(reader["scale"]),
                        ["is_nullable"] = Convert.ToBoolean(reader["is_nullable"]),
                        ["is_identity"] = Convert.ToBoolean(reader["is_identity"]),
                        ["is_computed"] = Convert.ToBoolean(reader["is_computed"]),
                        ["computed_definition"] = reader["computed_definition"]?.ToString(),
                        ["default_constraint"] = reader["default_constraint"]?.ToString(),
                        ["description"] = reader["description"]?.ToString(),
                        ["is_primary_key"] = Convert.ToBoolean(reader["is_primary_key"]),
                        ["is_foreign_key"] = Convert.ToBoolean(reader["is_foreign_key"]),
                        ["is_indexed"] = Convert.ToBoolean(reader["is_indexed"])
                    };

                    // Add formatted data type
                    var dataTypeName = reader["data_type"].ToString()!;
                    var maxLength = Convert.ToInt32(reader["max_length"]);
                    var precision = Convert.ToInt32(reader["precision"]);
                    var scale = Convert.ToInt32(reader["scale"]);

                    string formattedDataType;
                    if (dataTypeName.Contains("char") || dataTypeName.Contains("binary"))
                    {
                        formattedDataType = maxLength == -1 ? $"{dataTypeName}(MAX)" : $"{dataTypeName}({maxLength})";
                    }
                    else if (dataTypeName.Contains("decimal") || dataTypeName.Contains("numeric"))
                    {
                        formattedDataType = $"{dataTypeName}({precision},{scale})";
                    }
                    else
                    {
                        formattedDataType = dataTypeName;
                    }
                    column["formatted_data_type"] = formattedDataType;

                    ((List<Dictionary<string, object>>)tables[tableKey]["columns"]).Add(column);

                    // Update summary
                    summary["total_columns"] = Convert.ToInt32(summary["total_columns"]) + 1;

                    var dataTypeDict = (Dictionary<string, int>)summary["data_type_distribution"];
                    if (!dataTypeDict.ContainsKey(dataTypeName))
                        dataTypeDict[dataTypeName] = 0;
                    dataTypeDict[dataTypeName]++;

                    var schemaDict = (Dictionary<string, int>)summary["schema_distribution"];
                    if (!schemaDict.ContainsKey(schemaNameValue))
                        schemaDict[schemaNameValue] = 0;
                    schemaDict[schemaNameValue]++;
                }

                summary["total_tables"] = tables.Count;

                var payload = new
                {
                    server_name = SqlConnectionManager.ServerName,
                    environment = SqlConnectionManager.Environment,
                    database = SqlConnectionManager.CurrentDatabase,
                    tables = tables.Values.ToList(),
                    summary = summary,
                    filters_applied = new
                    {
                        column_name = columnName,
                        schema_name = schemaName,
                        exact_match = exactMatch,
                        include_system_tables = includeSystemTables,
                        include_row_count = includeRowCount,
                        max_results = maxResults
                    }
                };
                sw.Stop();
                LoggingHelper.LogEnd(corr, "FindTablesWithColumn", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "FindTablesWithColumn");
                LoggingHelper.LogEnd(Guid.Empty, "FindTablesWithColumn", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "FindTablesWithColumn");
                LoggingHelper.LogEnd(Guid.Empty, "FindTablesWithColumn", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }
    }
}
