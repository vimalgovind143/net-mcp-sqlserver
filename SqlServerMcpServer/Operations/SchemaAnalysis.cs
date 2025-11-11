using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;
using SqlServerMcpServer.Configuration;
using SqlServerMcpServer.Utilities;
using System.ComponentModel;

namespace SqlServerMcpServer.Operations
{
    /// <summary>
    /// Handles schema analysis operations for relationships, indexes, constraints, and dependencies
    /// </summary>
    public static class SchemaAnalysis
    {
        /// <summary>
        /// Discover foreign key relationships between tables
        /// </summary>
        /// <param name="tableName">Table name to filter relationships (optional)</param>
        /// <param name="schemaName">Schema name (default: 'dbo')</param>
        /// <param name="includeReferencedBy">Include tables that reference this table (default: true)</param>
        /// <param name="includeReferences">Include tables this table references (default: true)</param>
        /// <returns>Table relationships as JSON string</returns>
        [McpServerTool, Description("Discover foreign key relationships between tables")]
        public static async Task<string> GetTableRelationships(
            [Description("Table name to filter relationships (optional)")] string? tableName = null,
            [Description("Schema name (default: 'dbo')")] string? schemaName = "dbo",
            [Description("Include tables that reference this table (default: true)")] bool includeReferencedBy = true,
            [Description("Include tables this table references (default: true)")] bool includeReferences = true)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("GetTableRelationships", $"table:{tableName}, schema:{schemaName}");
                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

                var query = @"
                    SELECT 
                        f.name AS constraint_name,
                        'REFERENCES' AS relationship_type,
                        OBJECT_NAME(f.parent_object_id) AS parent_table,
                        COL_NAME(fc.parent_object_id, fc.parent_column_id) AS parent_column,
                        OBJECT_NAME(f.referenced_object_id) AS child_table,
                        COL_NAME(fc.referenced_object_id, fc.referenced_column_id) AS child_column,
                        f.update_referential_action_desc AS update_rule,
                        f.delete_referential_action_desc AS delete_rule
                    FROM sys.foreign_keys AS f
                    INNER JOIN sys.foreign_key_columns AS fc ON f.object_id = fc.constraint_object_id
                    WHERE (@TableName IS NULL OR OBJECT_NAME(f.parent_object_id) = @TableName OR OBJECT_NAME(f.referenced_object_id) = @TableName)
                    AND (@SchemaName IS NULL OR SCHEMA_NAME(OBJECT_SCHEMA_ID(f.parent_object_id)) = @SchemaName OR SCHEMA_NAME(OBJECT_SCHEMA_ID(f.referenced_object_id)) = @SchemaName)
                    ORDER BY parent_table, child_table, constraint_name";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@TableName", (object?)tableName ?? DBNull.Value);
                command.Parameters.AddWithValue("@SchemaName", (object?)schemaName ?? DBNull.Value);

                using var reader = await command.ExecuteReaderAsync();
                
                var relationships = new List<Dictionary<string, object>>();
                var relationshipGroups = new Dictionary<string, Dictionary<string, object>>();

                while (await reader.ReadAsync())
                {
                    var constraintName = reader["constraint_name"].ToString()!;
                    var relationshipType = reader["relationship_type"].ToString()!;
                    var parentTable = reader["parent_table"].ToString()!;
                    var parentColumn = reader["parent_column"].ToString()!;
                    var childTable = reader["child_table"].ToString()!;
                    var childColumn = reader["child_column"].ToString()!;
                    var updateRule = reader["update_rule"].ToString()!;
                    var deleteRule = reader["delete_rule"].ToString()!;

                    // Filter based on parameters
                    if (!string.IsNullOrEmpty(tableName))
                    {
                        if (!includeReferences && parentTable.Equals(tableName, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!includeReferencedBy && childTable.Equals(tableName, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    if (!relationshipGroups.ContainsKey(constraintName))
                    {
                        relationshipGroups[constraintName] = new Dictionary<string, object>
                        {
                            ["constraint_name"] = constraintName,
                            ["relationship_type"] = parentTable.Equals(tableName, StringComparison.OrdinalIgnoreCase) ? "REFERENCES" : "REFERENCED_BY",
                            ["parent_table"] = parentTable,
                            ["parent_columns"] = new List<string>(),
                            ["child_table"] = childTable,
                            ["child_columns"] = new List<string>(),
                            ["update_rule"] = updateRule,
                            ["delete_rule"] = deleteRule
                        };
                    }

                    ((List<string>)relationshipGroups[constraintName]["parent_columns"]).Add(parentColumn);
                    ((List<string>)relationshipGroups[constraintName]["child_columns"]).Add(childColumn);
                }

                var payload = new
                {
                    server_name = SqlConnectionManager.ServerName,
                    environment = SqlConnectionManager.Environment,
                    database = SqlConnectionManager.CurrentDatabase,
                    relationship_count = relationshipGroups.Count,
                    filters_applied = new
                    {
                        table_name = tableName,
                        schema_name = schemaName,
                        include_referenced_by = includeReferencedBy,
                        include_references = includeReferences
                    },
                    relationships = relationshipGroups.Values.ToList()
                };
                sw.Stop();
                LoggingHelper.LogEnd(corr, "GetTableRelationships", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "GetTableRelationships");
                LoggingHelper.LogEnd(Guid.Empty, "GetTableRelationships", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "GetTableRelationships");
                LoggingHelper.LogEnd(Guid.Empty, "GetTableRelationships", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }

        /// <summary>
        /// List indexes with usage statistics
        /// </summary>
        /// <param name="tableName">Table name to filter indexes (optional)</param>
        /// <param name="schemaName">Schema name (default: 'dbo')</param>
        /// <param name="includeStatistics">Include usage statistics (default: true)</param>
        /// <returns>Index information as JSON string</returns>
        [McpServerTool, Description("List indexes with usage statistics")]
        public static async Task<string> GetIndexInformation(
            [Description("Table name to filter indexes (optional)")] string? tableName = null,
            [Description("Schema name (default: 'dbo')")] string? schemaName = "dbo",
            [Description("Include usage statistics (default: true)")] bool includeStatistics = true)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("GetIndexInformation", $"table:{tableName}, schema:{schemaName}");
                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

                var query = includeStatistics ? @"
                    SELECT 
                        t.name AS table_name,
                        i.name AS index_name,
                        i.type_desc AS index_type,
                        i.is_unique,
                        i.is_primary_key,
                        i.is_unique_constraint,
                        STRING_AGG(col.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS key_columns,
                        STRING_AGG(inc_col.name, ', ') AS included_columns,
                        ISNULL(us.user_seeks, 0) AS user_seeks,
                        ISNULL(us.user_scans, 0) AS user_scans,
                        ISNULL(us.user_lookups, 0) AS user_lookups,
                        ISNULL(us.user_updates, 0) AS user_updates,
                        CONVERT(DECIMAL(10,2), (ISNULL(ps.used_page_count, 0) * 8.0) / 1024.0) AS size_mb
                    FROM sys.tables t
                    INNER JOIN sys.indexes i ON t.object_id = i.object_id
                    INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id AND ic.is_included_column = 0
                    INNER JOIN sys.columns col ON ic.object_id = col.object_id AND ic.column_id = col.column_id
                    LEFT JOIN sys.index_columns inc_col ON i.object_id = inc_col.object_id AND i.index_id = inc_col.index_id AND inc_col.is_included_column = 1
                    LEFT JOIN sys.columns inc_col_def ON inc_col.object_id = inc_col_def.object_id AND inc_col.column_id = inc_col_def.column_id
                    LEFT JOIN sys.dm_db_index_usage_stats us ON i.object_id = us.object_id AND i.index_id = us.index_id AND us.database_id = DB_ID()
                    LEFT JOIN sys.dm_db_partition_stats ps ON i.object_id = ps.object_id AND i.index_id = ps.index_id
                    WHERE (@TableName IS NULL OR t.name = @TableName)
                    AND (@SchemaName IS NULL OR SCHEMA_NAME(t.schema_id) = @SchemaName)
                    AND i.name IS NOT NULL
                    GROUP BY t.name, i.name, i.type_desc, i.is_unique, i.is_primary_key, i.is_unique_constraint, 
                             us.user_seeks, us.user_scans, us.user_lookups, us.user_updates, ps.used_page_count
                    ORDER BY t.name, i.name" : @"
                    SELECT 
                        t.name AS table_name,
                        i.name AS index_name,
                        i.type_desc AS index_type,
                        i.is_unique,
                        i.is_primary_key,
                        i.is_unique_constraint,
                        STRING_AGG(col.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS key_columns,
                        '' AS included_columns,
                        0 AS user_seeks,
                        0 AS user_scans,
                        0 AS user_lookups,
                        0 AS user_updates,
                        0 AS size_mb
                    FROM sys.tables t
                    INNER JOIN sys.indexes i ON t.object_id = i.object_id
                    INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id AND ic.is_included_column = 0
                    INNER JOIN sys.columns col ON ic.object_id = col.object_id AND ic.column_id = col.column_id
                    WHERE (@TableName IS NULL OR t.name = @TableName)
                    AND (@SchemaName IS NULL OR SCHEMA_NAME(t.schema_id) = @SchemaName)
                    AND i.name IS NOT NULL
                    GROUP BY t.name, i.name, i.type_desc, i.is_unique, i.is_primary_key, i.is_unique_constraint
                    ORDER BY t.name, i.name";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@TableName", (object?)tableName ?? DBNull.Value);
                command.Parameters.AddWithValue("@SchemaName", (object?)schemaName ?? DBNull.Value);

                using var reader = await command.ExecuteReaderAsync();
                
                var indexes = new List<Dictionary<string, object>>();
                while (await reader.ReadAsync())
                {
                    var index = new Dictionary<string, object>
                    {
                        ["table_name"] = reader["table_name"].ToString()!,
                        ["index_name"] = reader["index_name"].ToString()!,
                        ["index_type"] = reader["index_type"].ToString()!,
                        ["is_unique"] = Convert.ToBoolean(reader["is_unique"]),
                        ["key_columns"] = reader["key_columns"].ToString()!.Split(", ", StringSplitOptions.RemoveEmptyEntries).ToList(),
                        ["included_columns"] = reader["included_columns"].ToString()!.Split(", ", StringSplitOptions.RemoveEmptyEntries).ToList(),
                        ["user_seeks"] = Convert.ToInt64(reader["user_seeks"]),
                        ["user_scans"] = Convert.ToInt64(reader["user_scans"]),
                        ["size_mb"] = Convert.ToDecimal(reader["size_mb"])
                    };
                    indexes.Add(index);
                }

                var payload = new
                {
                    server_name = SqlConnectionManager.ServerName,
                    environment = SqlConnectionManager.Environment,
                    database = SqlConnectionManager.CurrentDatabase,
                    index_count = indexes.Count,
                    filters_applied = new
                    {
                        table_name = tableName,
                        schema_name = schemaName,
                        include_statistics = includeStatistics
                    },
                    indexes = indexes
                };
                sw.Stop();
                LoggingHelper.LogEnd(corr, "GetIndexInformation", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "GetIndexInformation");
                LoggingHelper.LogEnd(Guid.Empty, "GetIndexInformation", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "GetIndexInformation");
                LoggingHelper.LogEnd(Guid.Empty, "GetIndexInformation", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }
    }
}
