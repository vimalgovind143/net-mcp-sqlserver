using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;
using SqlServerMcpServer.Configuration;
using SqlServerMcpServer.Utilities;
using System.ComponentModel;

namespace SqlServerMcpServer.Operations
{
    /// <summary>
    /// Handles schema inspection operations for tables, procedures, and views
    /// </summary>
    public static class SchemaInspection
    {
        /// <summary>
        /// Get a list of all tables in the current database with size information and filtering options
        /// </summary>
        /// <param name="schemaFilter">Filter tables by schema name (optional)</param>
        /// <param name="nameFilter">Filter by table name (partial match, optional)</param>
        /// <param name="minRowCount">Minimum row count filter (optional)</param>
        /// <param name="sortBy">Sort by: 'NAME', 'SIZE', or 'ROWS' (default: 'NAME')</param>
        /// <param name="sortOrder">Sort order: 'ASC' or 'DESC' (default: 'ASC')</param>
        /// <returns>Table list as JSON string</returns>
        [McpServerTool, Description("Get a list of all tables in the current database with size information and filtering options")]
        public static async Task<string> GetTablesAsync(
            [Description("Filter tables by schema name (optional)")] string? schemaFilter = null,
            [Description("Filter by table name (partial match, optional)")] string? nameFilter = null,
            [Description("Minimum row count filter (optional)")] int? minRowCount = null,
            [Description("Sort by: 'NAME', 'SIZE', or 'ROWS' (default: 'NAME')")] string? sortBy = "NAME",
            [Description("Sort order: 'ASC' or 'DESC' (default: 'ASC')")] string? sortOrder = "ASC")
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("GetTables", $"schema:{schemaFilter}, name:{nameFilter}, minRows:{minRowCount}, sort:{sortBy} {sortOrder}");
                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

                // Validate sort parameters
                sortBy = sortBy?.ToUpperInvariant() ?? "NAME";
                sortOrder = sortOrder?.ToUpperInvariant() ?? "ASC";

                if (!new[] { "NAME", "SIZE", "ROWS" }.Contains(sortBy))
                    sortBy = "NAME";
                if (!new[] { "ASC", "DESC" }.Contains(sortOrder))
                    sortOrder = "ASC";

                // Build dynamic WHERE clause for filters
                var whereConditions = new List<string>();
                if (!string.IsNullOrEmpty(schemaFilter))
                {
                    whereConditions.Add("s.name = @schemaFilter");
                }
                if (!string.IsNullOrEmpty(nameFilter))
                {
                    whereConditions.Add("t.name LIKE @nameFilter");
                }
                if (minRowCount.HasValue && minRowCount.Value > 0)
                {
                    whereConditions.Add("p.rows >= @minRowCount");
                }

                // Always include the index condition
                whereConditions.Add("i.index_id IN (0,1)");

                var whereClause = "WHERE " + string.Join(" AND ", whereConditions);

                // Build dynamic ORDER BY clause
                var orderByClause = sortBy switch
                {
                    "SIZE" => $"ORDER BY total_size_mb {(sortOrder == "DESC" ? "DESC" : "ASC")}, s.name, t.name",
                    "ROWS" => $"ORDER BY p.rows {(sortOrder == "DESC" ? "DESC" : "ASC")}, s.name, t.name",
                    _ => $"ORDER BY s.name {(sortOrder == "DESC" ? "DESC" : "ASC")}, t.name"
                };

                var query = $@"
                    SELECT
                        t.name AS table_name,
                        s.name AS schema_name,
                        p.rows AS row_count,
                        SUM(a.total_pages) * 8 / 1024.0 AS total_size_mb,
                        SUM(a.used_pages) * 8 / 1024.0 AS used_size_mb,
                        SUM(a.data_pages) * 8 / 1024.0 AS data_size_mb,
                        (SUM(a.total_pages) - SUM(a.used_pages)) * 8 / 1024.0 AS unused_size_mb,
                        t.create_date,
                        t.modify_date,
                        t.is_memory_optimized,
                        t.temporal_type_desc,
                        (
                            SELECT COUNT(*)
                            FROM sys.indexes i
                            WHERE i.object_id = t.object_id AND i.is_primary_key = 1
                        ) AS has_primary_key,
                        (
                            SELECT COUNT(*)
                            FROM sys.indexes i
                            WHERE i.object_id = t.object_id AND i.type = 1
                        ) AS has_clustered_index,
                        (
                            SELECT COUNT(*)
                            FROM sys.foreign_keys fk
                            WHERE fk.parent_object_id = t.object_id
                        ) AS foreign_key_count,
                        (
                            SELECT COUNT(*)
                            FROM sys.foreign_keys fk
                            WHERE fk.referenced_object_id = t.object_id
                        ) AS referenced_by_count
                    FROM sys.tables t
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    INNER JOIN sys.indexes i ON t.object_id = i.object_id
                    INNER JOIN sys.partitions p ON i.object_id = p.object_id AND i.index_id = p.index_id
                    INNER JOIN sys.allocation_units a ON p.partition_id = a.container_id
                    {whereClause}
                    GROUP BY t.object_id, t.name, s.name, p.rows, t.create_date, t.modify_date, t.is_memory_optimized, t.temporal_type_desc
                    {orderByClause}";

                using var command = new SqlCommand(query, connection)
                {
                    CommandTimeout = SqlConnectionManager.CommandTimeout
                };

                if (!string.IsNullOrEmpty(schemaFilter))
                    command.Parameters.AddWithValue("@schemaFilter", schemaFilter);
                if (!string.IsNullOrEmpty(nameFilter))
                    command.Parameters.AddWithValue("@nameFilter", $"%{nameFilter}%");
                if (minRowCount.HasValue)
                    command.Parameters.AddWithValue("@minRowCount", minRowCount.Value);

                using var reader = await command.ExecuteReaderAsync();

                var tables = new List<Dictionary<string, object>>();

                while (await reader.ReadAsync())
                {
                    var table = new Dictionary<string, object>
                    {
                        ["table_name"] = reader["table_name"],
                        ["schema_name"] = reader["schema_name"],
                        ["row_count"] = reader["row_count"],
                        ["total_size_mb"] = Math.Round(Convert.ToDecimal(reader["total_size_mb"]), 2),
                        ["used_size_mb"] = Math.Round(Convert.ToDecimal(reader["used_size_mb"]), 2),
                        ["data_size_mb"] = Math.Round(Convert.ToDecimal(reader["data_size_mb"]), 2),
                        ["unused_size_mb"] = Math.Round(Convert.ToDecimal(reader["unused_size_mb"]), 2),
                        ["create_date"] = reader["create_date"],
                        ["modify_date"] = reader["modify_date"],
                        ["is_memory_optimized"] = reader["is_memory_optimized"],
                        ["temporal_type_desc"] = reader["temporal_type_desc"] is DBNull ? null : reader["temporal_type_desc"],
                        ["index_summary"] = new Dictionary<string, object>
                        {
                            ["index_count"] = 0, // Will be calculated separately if needed
                            ["has_primary_key"] = Convert.ToInt32(reader["has_primary_key"]) > 0,
                            ["has_clustered_index"] = Convert.ToInt32(reader["has_clustered_index"]) > 0
                        },
                        ["relationship_summary"] = new Dictionary<string, object>
                        {
                            ["foreign_keys_referencing"] = reader["foreign_key_count"],
                            ["foreign_keys_referenced_by"] = reader["referenced_by_count"]
                        }
                    };
                    tables.Add(table);
                }

                var tablesData = new
                {
                    table_count = tables.Count,
                    filters_applied = new
                    {
                        schema_filter = schemaFilter,
                        name_filter = nameFilter,
                        min_row_count = minRowCount,
                        sort_by = sortBy,
                        sort_order = sortOrder
                    },
                    tables = tables
                };

                var metadata = new Dictionary<string, object>
                {
                    ["row_count"] = tables.Count,
                    ["page_count"] = 1
                };

                var payload = ResponseFormatter.CreateStandardResponse("GetTables", tablesData, sw.ElapsedMilliseconds, metadata: metadata);
                sw.Stop();
                LoggingHelper.LogEnd(corr, "GetTables", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "GetTables");
                LoggingHelper.LogEnd(Guid.Empty, "GetTables", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "GetTables");
                LoggingHelper.LogEnd(Guid.Empty, "GetTables", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }

        /// <summary>
        /// Get the schema information for a specific table with PK/FK info, indexes, and extended properties
        /// </summary>
        /// <param name="tableName">Name of the table</param>
        /// <param name="schemaName">Schema name (defaults to 'dbo')</param>
        /// <param name="includeStatistics">Include column statistics (optional, default: false)</param>
        /// <returns>Table schema as JSON string</returns>
        [McpServerTool, Description("Get the schema information for a specific table with PK/FK info, indexes, and extended properties")]
        public static async Task<string> GetTableSchemaAsync(
            [Description("Name of the table")] string tableName,
            [Description("Schema name (defaults to 'dbo')")] string? schemaName = "dbo",
            [Description("Include column statistics (optional, default: false)")] bool includeStatistics = false)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("GetTableSchema", $"table:{tableName}, schema:{schemaName}");
                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

                var query = @"
                    SELECT
                        c.name AS column_name,
                        t.name AS data_type,
                        c.max_length,
                        c.precision,
                        c.scale,
                        c.is_nullable,
                        c.is_identity,
                        c.is_computed,
                        CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS is_primary_key,
                        pk.key_ordinal AS pk_ordinal,
                        dc.definition AS default_value,
                        cc.definition AS computed_definition,
                        ep.value AS column_description
                    FROM sys.columns c
                    INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
                    INNER JOIN sys.tables tbl ON c.object_id = tbl.object_id
                    INNER JOIN sys.schemas s ON tbl.schema_id = s.schema_id
                    LEFT JOIN (
                        SELECT ic.object_id, ic.column_id, ic.key_ordinal
                        FROM sys.index_columns ic
                        INNER JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                        WHERE i.is_primary_key = 1
                    ) pk ON c.object_id = pk.object_id AND c.column_id = pk.column_id
                    LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
                    LEFT JOIN sys.computed_columns cc ON c.object_id = cc.object_id AND c.column_id = cc.column_id
                    LEFT JOIN sys.extended_properties ep ON ep.major_id = tbl.object_id
                        AND ep.minor_id = c.column_id AND ep.name = 'MS_Description'
                    WHERE tbl.name = @tableName AND s.name = @schemaName
                    ORDER BY c.column_id";

                using var command = new SqlCommand(query, connection)
                {
                    CommandTimeout = SqlConnectionManager.CommandTimeout
                };
                command.Parameters.AddWithValue("@tableName", tableName);
                command.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                using var reader = await command.ExecuteReaderAsync();

                var columns = new List<Dictionary<string, object>>();

                while (await reader.ReadAsync())
                {
                    var column = new Dictionary<string, object>
                    {
                        ["column_name"] = reader["column_name"] ?? "",
                        ["data_type"] = reader["data_type"] ?? "",
                        ["max_length"] = reader["max_length"] ?? 0,
                        ["precision"] = reader["precision"] ?? 0,
                        ["scale"] = reader["scale"] ?? 0,
                        ["is_nullable"] = reader["is_nullable"] ?? false,
                        ["is_identity"] = reader["is_identity"] ?? false,
                        ["is_computed"] = reader["is_computed"] ?? false,
                        ["is_primary_key"] = reader["is_primary_key"] ?? false,
                        ["pk_ordinal"] = reader["pk_ordinal"] is DBNull ? null : reader["pk_ordinal"],
                        ["default_value"] = reader["default_value"] is DBNull ? null : reader["default_value"],
                        ["computed_definition"] = reader["computed_definition"] is DBNull ? null : reader["computed_definition"],
                        ["column_description"] = reader["column_description"] is DBNull ? null : reader["column_description"]
                    };
                    columns.Add(column);
                }

                reader.Close();

                // Get foreign key information
                var foreignKeysQuery = @"
                    SELECT
                        fk.name AS constraint_name,
                        c1.name AS column_name,
                        t2.name AS referenced_table,
                        c2.name AS referenced_column
                    FROM sys.foreign_keys fk
                    INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
                    INNER JOIN sys.columns c1 ON fkc.parent_object_id = c1.object_id AND fkc.parent_column_id = c1.column_id
                    INNER JOIN sys.columns c2 ON fkc.referenced_object_id = c2.object_id AND fkc.referenced_column_id = c2.column_id
                    INNER JOIN sys.tables t1 ON fkc.parent_object_id = t1.object_id
                    INNER JOIN sys.tables t2 ON fkc.referenced_object_id = t2.object_id
                    INNER JOIN sys.schemas s1 ON t1.schema_id = s1.schema_id
                    WHERE t1.name = @tableName AND s1.name = @schemaName
                    ORDER BY fk.name, fkc.constraint_column_id";

                var foreignKeys = new List<Dictionary<string, object>>();
                using (var fkCommand = new SqlCommand(foreignKeysQuery, connection)
                {
                    CommandTimeout = SqlConnectionManager.CommandTimeout
                })
                {
                    fkCommand.Parameters.AddWithValue("@tableName", tableName);
                    fkCommand.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                    using var fkReader = await fkCommand.ExecuteReaderAsync();
                    while (await fkReader.ReadAsync())
                    {
                        var fk = new Dictionary<string, object>
                        {
                            ["column_name"] = fkReader["column_name"],
                            ["referenced_table"] = fkReader["referenced_table"],
                            ["referenced_column"] = fkReader["referenced_column"],
                            ["constraint_name"] = fkReader["constraint_name"]
                        };
                        foreignKeys.Add(fk);
                    }
                }

                // Get index information per column
                var indexesQuery = @"
                    SELECT
                        i.name AS index_name,
                        i.type_desc AS index_type,
                        i.is_unique,
                        c.name AS column_name,
                        ic.key_ordinal
                    FROM sys.indexes i
                    INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                    INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                    INNER JOIN sys.tables t ON i.object_id = t.object_id
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    WHERE t.name = @tableName AND s.name = @schemaName AND i.name IS NOT NULL
                    ORDER BY i.name, ic.key_ordinal";

                var indexes = new List<Dictionary<string, object>>();
                using (var idxCommand = new SqlCommand(indexesQuery, connection)
                {
                    CommandTimeout = SqlConnectionManager.CommandTimeout
                })
                {
                    idxCommand.Parameters.AddWithValue("@tableName", tableName);
                    idxCommand.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                    using var idxReader = await idxCommand.ExecuteReaderAsync();
                    while (await idxReader.ReadAsync())
                    {
                        var idx = new Dictionary<string, object>
                        {
                            ["index_name"] = idxReader["index_name"],
                            ["index_type"] = idxReader["index_type"],
                            ["is_unique"] = idxReader["is_unique"],
                            ["key_ordinal"] = idxReader["key_ordinal"]
                        };
                        indexes.Add(idx);
                    }
                }

                // Get column statistics if requested
                var columnStats = new List<Dictionary<string, object>>();
                if (includeStatistics)
                {
                    try
                    {
                        var statsQuery = @"
                            SELECT
                                c.name AS column_name,
                                p.rows AS total_rows,
                                CASE WHEN c.is_nullable = 1 THEN 'NULLABLE' ELSE 'NOT NULL' END AS nullability
                            FROM sys.columns c
                            INNER JOIN sys.tables t ON c.object_id = t.object_id
                            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                            INNER JOIN sys.dm_db_partition_stats p ON t.object_id = p.object_id
                            WHERE t.name = @tableName AND s.name = @schemaName AND p.index_id IN (0,1)";

                        using (var statsCommand = new SqlCommand(statsQuery, connection)
                        {
                            CommandTimeout = SqlConnectionManager.CommandTimeout
                        })
                        {
                            statsCommand.Parameters.AddWithValue("@tableName", tableName);
                            statsCommand.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                            using var statsReader = await statsCommand.ExecuteReaderAsync();
                            while (await statsReader.ReadAsync())
                            {
                                var stat = new Dictionary<string, object>
                                {
                                    ["column_name"] = statsReader["column_name"],
                                    ["total_rows"] = statsReader["total_rows"],
                                    ["nullability"] = statsReader["nullability"]
                                };
                                columnStats.Add(stat);
                            }
                        }
                    }
                    catch
                    {
                        // Statistics might not be available for all columns
                        columnStats.Add(new Dictionary<string, object>
                        {
                            ["error"] = "Statistics not available for some columns"
                        });
                    }
                }

                var schemaData = new
                {
                    table_name = tableName,
                    schema_name = schemaName,
                    column_count = columns.Count,
                    columns = columns,
                    foreign_keys = foreignKeys,
                    indexes_using_column = indexes,
                    column_statistics = includeStatistics ? columnStats : null
                };

                var metadata = new Dictionary<string, object>
                {
                    ["row_count"] = 1,
                    ["page_count"] = 1
                };

                var payload = ResponseFormatter.CreateStandardResponse("GetTableSchema", schemaData, sw.ElapsedMilliseconds, metadata: metadata);
                sw.Stop();
                LoggingHelper.LogEnd(corr, "GetTableSchema", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "GetTableSchema");
                LoggingHelper.LogEnd(Guid.Empty, "GetTableSchema", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "GetTableSchema");
                LoggingHelper.LogEnd(Guid.Empty, "GetTableSchema", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }

        /// <summary>
        /// Get a list of stored procedures in the current database
        /// </summary>
        /// <param name="nameFilter">Filter by procedure name (partial match, optional)</param>
        /// <returns>Stored procedure list as JSON string</returns>
        [McpServerTool, Description("Get a list of stored procedures in the current database")]
        public static async Task<string> GetStoredProceduresAsync(
            [Description("Filter by procedure name (partial match, optional)")] string? nameFilter = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("GetStoredProcedures", $"name:{nameFilter}");
                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

                var query = $@"
                    SELECT
                        p.name AS procedure_name,
                        s.name AS schema_name,
                        p.create_date,
                        p.modify_date
                    FROM sys.procedures p
                    INNER JOIN sys.schemas s ON p.schema_id = s.schema_id
                    {(string.IsNullOrEmpty(nameFilter) ? "" : "WHERE p.name LIKE @nameFilter")}
                    ORDER BY s.name, p.name";

                using var command = new SqlCommand(query, connection)
                {
                    CommandTimeout = SqlConnectionManager.CommandTimeout
                };
                if (!string.IsNullOrEmpty(nameFilter))
                    command.Parameters.AddWithValue("@nameFilter", $"%{nameFilter}%");
                using var reader = await command.ExecuteReaderAsync();

                var procedures = new List<Dictionary<string, object>>();

                while (await reader.ReadAsync())
                {
                    var proc = new Dictionary<string, object>
                    {
                        ["procedure_name"] = reader["procedure_name"],
                        ["schema_name"] = reader["schema_name"],
                        ["create_date"] = reader["create_date"],
                        ["modify_date"] = reader["modify_date"]
                    };
                    procedures.Add(proc);
                }

                var payload = new
                {
                    server_name = SqlConnectionManager.ServerName,
                    environment = SqlConnectionManager.Environment,
                    database = SqlConnectionManager.CurrentDatabase,
                    procedure_count = procedures.Count,
                    filters_applied = new
                    {
                        name_filter = nameFilter
                    },
                    procedures = procedures
                };
                sw.Stop();
                LoggingHelper.LogEnd(corr, "GetStoredProcedures", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "GetStoredProcedures");
                LoggingHelper.LogEnd(Guid.Empty, "GetStoredProcedures", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "GetStoredProcedures");
                LoggingHelper.LogEnd(Guid.Empty, "GetStoredProcedures", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }

        /// <summary>
        /// Get detailed information about a specific stored procedure including parameters and definition
        /// </summary>
        /// <param name="procedureName">Name of the stored procedure</param>
        /// <param name="schemaName">Schema name (defaults to 'dbo')</param>
        /// <returns>Stored procedure details as JSON string</returns>
        [McpServerTool, Description("Get detailed information about a specific stored procedure including parameters and definition")]
        public static async Task<string> GetStoredProcedureDetailsAsync(
            [Description("Name of the stored procedure")] string procedureName,
            [Description("Schema name (defaults to 'dbo')")] string? schemaName = "dbo")
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("GetStoredProcedureDetails", $"{schemaName}.{procedureName}");
                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

                // Get stored procedure parameters
                var parametersQuery = @"
                    SELECT
                        p.name AS parameter_name,
                        t.name AS data_type,
                        p.max_length,
                        p.precision,
                        p.scale,
                        p.is_output,
                        p.has_default_value,
                        p.default_value
                    FROM sys.parameters p
                    INNER JOIN sys.types t ON p.user_type_id = t.user_type_id
                    INNER JOIN sys.procedures pr ON p.object_id = pr.object_id
                    INNER JOIN sys.schemas s ON pr.schema_id = s.schema_id
                    WHERE pr.name = @procedureName AND s.name = @schemaName
                    ORDER BY p.parameter_id";

                using var paramsCommand = new SqlCommand(parametersQuery, connection)
                {
                    CommandTimeout = SqlConnectionManager.CommandTimeout
                };
                paramsCommand.Parameters.AddWithValue("@procedureName", procedureName);
                paramsCommand.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                var parameters = new List<Dictionary<string, object>>();
                using (var reader = await paramsCommand.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var param = new Dictionary<string, object>
                        {
                            ["parameter_name"] = reader["parameter_name"] ?? "",
                            ["data_type"] = reader["data_type"] ?? "",
                            ["max_length"] = reader["max_length"] ?? 0,
                            ["precision"] = reader["precision"] ?? 0,
                            ["scale"] = reader["scale"] ?? 0,
                            ["is_output"] = reader["is_output"] ?? false,
                            ["has_default_value"] = reader["has_default_value"] ?? false,
                            ["default_value"] = reader["default_value"] is DBNull ? null : reader["default_value"]
                        };
                        parameters.Add(param);
                    }
                }

                // Get stored procedure definition and metadata
                var definitionQuery = @"
                    SELECT
                        pr.name AS procedure_name,
                        s.name AS schema_name,
                        pr.create_date,
                        pr.modify_date,
                        OBJECT_DEFINITION(pr.object_id) AS definition,
                        pr.is_ms_shipped,
                        pr.is_published,
                        pr.is_schema_published
                    FROM sys.procedures pr
                    INNER JOIN sys.schemas s ON pr.schema_id = s.schema_id
                    WHERE pr.name = @procedureName AND s.name = @schemaName";

                using var defCommand = new SqlCommand(definitionQuery, connection)
                {
                    CommandTimeout = SqlConnectionManager.CommandTimeout
                };
                defCommand.Parameters.AddWithValue("@procedureName", procedureName);
                defCommand.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                Dictionary<string, object> procedureInfo = null;
                using (var reader = await defCommand.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        procedureInfo = new Dictionary<string, object>
                        {
                            ["procedure_name"] = reader["procedure_name"],
                            ["schema_name"] = reader["schema_name"],
                            ["create_date"] = reader["create_date"],
                            ["modify_date"] = reader["modify_date"],
                            ["definition"] = reader["definition"] is DBNull ? "Definition not available (may be encrypted)" : reader["definition"],
                            ["is_ms_shipped"] = reader["is_ms_shipped"],
                            ["is_published"] = reader["is_published"],
                            ["is_schema_published"] = reader["is_schema_published"]
                        };
                    }
                }

                if (procedureInfo == null)
                {
                    sw.Stop();
                    LoggingHelper.LogEnd(corr, "GetStoredProcedureDetails", false, sw.ElapsedMilliseconds, "Stored procedure not found");
                    return ResponseFormatter.ToJson(new
                    {
                        server_name = SqlConnectionManager.ServerName,
                        environment = SqlConnectionManager.Environment,
                        database = SqlConnectionManager.CurrentDatabase,
                        error = $"Stored procedure '{schemaName}.{procedureName}' not found",
                        operation_type = "NOT_FOUND",
                        security_mode = "READ_ONLY_ENFORCED"
                    });
                }

                // Get dependencies (objects this procedure references)
                var dependenciesQuery = @"
                    SELECT DISTINCT
                        OBJECT_NAME(d.referenced_major_id) AS referenced_object_name,
                        o.type_desc AS object_type
                    FROM sys.sql_dependencies d
                    INNER JOIN sys.procedures pr ON d.object_id = pr.object_id
                    INNER JOIN sys.schemas s ON pr.schema_id = s.schema_id
                    LEFT JOIN sys.objects o ON d.referenced_major_id = o.object_id
                    WHERE pr.name = @procedureName AND s.name = @schemaName
                    ORDER BY o.type_desc, OBJECT_NAME(d.referenced_major_id)";

                using var depsCommand = new SqlCommand(dependenciesQuery, connection)
                {
                    CommandTimeout = SqlConnectionManager.CommandTimeout
                };
                depsCommand.Parameters.AddWithValue("@procedureName", procedureName);
                depsCommand.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                var dependencies = new List<Dictionary<string, object>>();
                using (var reader = await depsCommand.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var dep = new Dictionary<string, object>
                        {
                            ["referenced_object_name"] = reader["referenced_object_name"] is DBNull ? null : reader["referenced_object_name"],
                            ["object_type"] = reader["object_type"] is DBNull ? "UNKNOWN" : reader["object_type"]
                        };
                        dependencies.Add(dep);
                    }
                }

                var payload = new
                {
                    server_name = SqlConnectionManager.ServerName,
                    environment = SqlConnectionManager.Environment,
                    database = SqlConnectionManager.CurrentDatabase,
                    procedure_info = procedureInfo,
                    parameter_count = parameters.Count,
                    parameters = parameters,
                    dependency_count = dependencies.Count,
                    dependencies = dependencies,
                    security_mode = "READ_ONLY"
                };

                sw.Stop();
                LoggingHelper.LogEnd(corr, "GetStoredProcedureDetails", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "GetStoredProcedureDetails");
                LoggingHelper.LogEnd(Guid.Empty, "GetStoredProcedureDetails", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "GetStoredProcedureDetails");
                LoggingHelper.LogEnd(Guid.Empty, "GetStoredProcedureDetails", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }

        /// <summary>
        /// Get detailed information about any database object (stored procedure, function, or view) including definition, parameters, and dependencies
        /// </summary>
        /// <param name="objectName">Name of the database object (procedure, function, or view)</param>
        /// <param name="schemaName">Schema name (defaults to 'dbo')</param>
        /// <param name="objectType">Object type: 'PROCEDURE', 'FUNCTION', 'VIEW', or 'AUTO' to auto-detect (default)</param>
        /// <returns>Object definition as JSON string</returns>
        [McpServerTool, Description("Get detailed information about any database object (stored procedure, function, or view) including definition, parameters, and dependencies")]
        public static async Task<string> GetObjectDefinitionAsync(
            [Description("Name of the database object (procedure, function, or view)")] string objectName,
            [Description("Schema name (defaults to 'dbo')")] string? schemaName = "dbo",
            [Description("Object type: 'PROCEDURE', 'FUNCTION', 'VIEW', or 'AUTO' to auto-detect (default)")] string? objectType = "AUTO")
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("GetObjectDefinition", $"{schemaName}.{objectName}");
                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

                // Auto-detect object type if needed
                string detectedType = null;
                if (objectType == null || objectType.ToUpperInvariant() == "AUTO")
                {
                    var typeQuery = @"
                        SELECT o.type_desc
                        FROM sys.objects o
                        INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                        WHERE o.name = @objectName AND s.name = @schemaName";

                    using var typeCmd = new SqlCommand(typeQuery, connection)
                    {
                        CommandTimeout = SqlConnectionManager.CommandTimeout
                    };
                    typeCmd.Parameters.AddWithValue("@objectName", objectName);
                    typeCmd.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                    var result = await typeCmd.ExecuteScalarAsync();
                    detectedType = result?.ToString();

                    if (detectedType == null)
                    {
                        sw.Stop();
                        LoggingHelper.LogEnd(corr, "GetObjectDefinition", false, sw.ElapsedMilliseconds, "Object not found");
                        return ResponseFormatter.ToJson(new
                        {
                            server_name = SqlConnectionManager.ServerName,
                            environment = SqlConnectionManager.Environment,
                            database = SqlConnectionManager.CurrentDatabase,
                            error = $"Object '{schemaName}.{objectName}' not found",
                            operation_type = "NOT_FOUND",
                            security_mode = "READ_ONLY_ENFORCED"
                        });
                    }
                }
                else
                {
                    detectedType = objectType.ToUpperInvariant();
                }

                // Get common object information
                var objectInfoQuery = @"
                    SELECT
                        o.name AS object_name,
                        s.name AS schema_name,
                        o.type_desc AS object_type,
                        o.create_date,
                        o.modify_date,
                        OBJECT_DEFINITION(o.object_id) AS definition
                    FROM sys.objects o
                    INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                    WHERE o.name = @objectName AND s.name = @schemaName";

                using var objCmd = new SqlCommand(objectInfoQuery, connection)
                {
                    CommandTimeout = SqlConnectionManager.CommandTimeout
                };
                objCmd.Parameters.AddWithValue("@objectName", objectName);
                objCmd.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                Dictionary<string, object> objectInfo = null;
                using (var reader = await objCmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        objectInfo = new Dictionary<string, object>
                        {
                            ["object_name"] = reader["object_name"],
                            ["schema_name"] = reader["schema_name"],
                            ["object_type"] = reader["object_type"],
                            ["create_date"] = reader["create_date"],
                            ["modify_date"] = reader["modify_date"],
                            ["definition"] = reader["definition"] is DBNull ? "Definition not available (may be encrypted)" : reader["definition"]
                        };
                    }
                }

                // Get parameters (for procedures and functions)
                var parameters = new List<Dictionary<string, object>>();
                if (detectedType.Contains("PROCEDURE") || detectedType.Contains("FUNCTION"))
                {
                    var parametersQuery = @"
                        SELECT
                            p.name AS parameter_name,
                            t.name AS data_type,
                            p.max_length,
                            p.precision,
                            p.scale,
                            p.is_output,
                            p.has_default_value,
                            p.default_value
                        FROM sys.parameters p
                        INNER JOIN sys.types t ON p.user_type_id = t.user_type_id
                        INNER JOIN sys.objects o ON p.object_id = o.object_id
                        INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                        WHERE o.name = @objectName AND s.name = @schemaName
                        ORDER BY p.parameter_id";

                    using var paramsCmd = new SqlCommand(parametersQuery, connection)
                    {
                        CommandTimeout = SqlConnectionManager.CommandTimeout
                    };
                    paramsCmd.Parameters.AddWithValue("@objectName", objectName);
                    paramsCmd.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                    using (var reader = await paramsCmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var param = new Dictionary<string, object>
                            {
                                ["parameter_name"] = reader["parameter_name"] ?? "",
                                ["data_type"] = reader["data_type"] ?? "",
                                ["max_length"] = reader["max_length"] ?? 0,
                                ["precision"] = reader["precision"] ?? 0,
                                ["scale"] = reader["scale"] ?? 0,
                                ["is_output"] = reader["is_output"] ?? false,
                                ["has_default_value"] = reader["has_default_value"] ?? false,
                                ["default_value"] = reader["default_value"] is DBNull ? null : reader["default_value"]
                            };
                            parameters.Add(param);
                        }
                    }
                }

                // Get columns (for views)
                var columns = new List<Dictionary<string, object>>();
                if (detectedType.Contains("VIEW"))
                {
                    var columnsQuery = @"
                        SELECT
                            c.name AS column_name,
                            t.name AS data_type,
                            c.max_length,
                            c.precision,
                            c.scale,
                            c.is_nullable,
                            c.is_identity,
                            c.is_computed
                        FROM sys.columns c
                        INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
                        INNER JOIN sys.views v ON c.object_id = v.object_id
                        INNER JOIN sys.schemas s ON v.schema_id = s.schema_id
                        WHERE v.name = @objectName AND s.name = @schemaName
                        ORDER BY c.column_id";

                    using var colsCmd = new SqlCommand(columnsQuery, connection)
                    {
                        CommandTimeout = SqlConnectionManager.CommandTimeout
                    };
                    colsCmd.Parameters.AddWithValue("@objectName", objectName);
                    colsCmd.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                    using (var reader = await colsCmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var col = new Dictionary<string, object>
                            {
                                ["column_name"] = reader["column_name"] ?? "",
                                ["data_type"] = reader["data_type"] ?? "",
                                ["max_length"] = reader["max_length"] ?? 0,
                                ["precision"] = reader["precision"] ?? 0,
                                ["scale"] = reader["scale"] ?? 0,
                                ["is_nullable"] = reader["is_nullable"] ?? false,
                                ["is_identity"] = reader["is_identity"] ?? false,
                                ["is_computed"] = reader["is_computed"] ?? false
                            };
                            columns.Add(col);
                        }
                    }
                }

                // Get dependencies
                var dependencies = new List<Dictionary<string, object>>();
                var dependenciesQuery = @"
                    SELECT DISTINCT
                        OBJECT_NAME(d.referenced_major_id) AS referenced_object_name,
                        o.type_desc AS object_type
                    FROM sys.sql_dependencies d
                    INNER JOIN sys.objects obj ON d.object_id = obj.object_id
                    INNER JOIN sys.schemas s ON obj.schema_id = s.schema_id
                    LEFT JOIN sys.objects o ON d.referenced_major_id = o.object_id
                    WHERE obj.name = @objectName AND s.name = @schemaName
                    ORDER BY o.type_desc, OBJECT_NAME(d.referenced_major_id)";

                using var depsCmd = new SqlCommand(dependenciesQuery, connection)
                {
                    CommandTimeout = SqlConnectionManager.CommandTimeout
                };
                depsCmd.Parameters.AddWithValue("@objectName", objectName);
                depsCmd.Parameters.AddWithValue("@schemaName", schemaName ?? "dbo");

                using (var reader = await depsCmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var dep = new Dictionary<string, object>
                        {
                            ["referenced_object_name"] = reader["referenced_object_name"] is DBNull ? null : reader["referenced_object_name"],
                            ["object_type"] = reader["object_type"] is DBNull ? "UNKNOWN" : reader["object_type"]
                        };
                        dependencies.Add(dep);
                    }
                }

                // Build response based on object type
                var payload = new Dictionary<string, object>
                {
                    ["server_name"] = SqlConnectionManager.ServerName,
                    ["environment"] = SqlConnectionManager.Environment,
                    ["database"] = SqlConnectionManager.CurrentDatabase,
                    ["object_info"] = objectInfo,
                    ["dependency_count"] = dependencies.Count,
                    ["dependencies"] = dependencies,
                    ["security_mode"] = "READ_ONLY"
                };

                if (detectedType.Contains("PROCEDURE") || detectedType.Contains("FUNCTION"))
                {
                    payload["parameter_count"] = parameters.Count;
                    payload["parameters"] = parameters;
                }

                if (detectedType.Contains("VIEW"))
                {
                    payload["column_count"] = columns.Count;
                    payload["columns"] = columns;
                }

                sw.Stop();
                LoggingHelper.LogEnd(corr, "GetObjectDefinition", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "GetObjectDefinition");
                LoggingHelper.LogEnd(Guid.Empty, "GetObjectDefinition", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "GetObjectDefinition");
                LoggingHelper.LogEnd(Guid.Empty, "GetObjectDefinition", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }
    }
}
