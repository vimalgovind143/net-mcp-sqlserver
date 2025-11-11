using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;
using SqlServerMcpServer.Configuration;
using SqlServerMcpServer.Security;
using SqlServerMcpServer.Utilities;
using System.ComponentModel;
using System.Text;

namespace SqlServerMcpServer.Operations
{
    /// <summary>
    /// Handles code generation operations for models, queries, and procedures
    /// </summary>
    public static class CodeGeneration
    {
        /// <summary>
        /// Generate C# model class code based on table schema for .NET applications
        /// </summary>
        /// <param name="tableName">Table name (required)</param>
        /// <param name="schemaName">Schema name (default: 'dbo')</param>
        /// <param name="className">Custom class name (optional - defaults to table name)</param>
        /// <param name="namespace">Namespace for C# (default: 'GeneratedModels')</param>
        /// <param name="includeValidation">Include DataAnnotations validation attributes (default: true)</param>
        /// <param name="includeAnnotations">Include Table/Column attributes and XML documentation (default: true)</param>
        /// <returns>Generated C# model class code as JSON string</returns>
        [McpServerTool, Description("Generate C# model class code based on table schema for .NET applications")]
        public static async Task<string> GenerateModelClass(
            [Description("Table name (required)")] string tableName,
            [Description("Schema name (default: 'dbo')")] string? schemaName = "dbo",
            [Description("Custom class name (optional - defaults to table name)")] string? className = null,
            [Description("Namespace for C# (default: 'GeneratedModels')")] string @namespace = "GeneratedModels",
            [Description("Include validation attributes (default: true)")] bool includeValidation = true,
            [Description("Include documentation annotations (default: true)")] bool includeAnnotations = true)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var corr = LoggingHelper.LogStart("GenerateModelClass", $"table:{tableName}");
                using var connection = SqlConnectionManager.CreateConnection();
                await connection.OpenAsync();

                // Get table schema
                var schemaQuery = @"
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
                        ep.value AS description,
                        CASE 
                            WHEN pk.column_id IS NOT NULL THEN 1
                            ELSE 0
                        END AS is_primary_key
                    FROM sys.columns c
                    INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
                    INNER JOIN sys.tables tbl ON c.object_id = tbl.object_id
                    INNER JOIN sys.schemas s ON tbl.schema_id = s.schema_id
                    LEFT JOIN sys.computed_columns cc ON c.object_id = cc.object_id AND c.column_id = cc.column_id
                    LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
                    LEFT JOIN sys.extended_properties ep ON c.object_id = ep.major_id AND c.column_id = ep.minor_id AND ep.name = 'MS_Description'
                    LEFT JOIN sys.index_columns pk ON c.object_id = pk.object_id AND c.column_id = pk.column_id AND pk.index_id IN (
                        SELECT index_id FROM sys.indexes WHERE object_id = c.object_id AND is_primary_key = 1
                    )
                    WHERE tbl.name = @TableName
                    AND s.name = @SchemaName
                    ORDER BY c.column_id";

                using var command = new SqlCommand(schemaQuery, connection);
                command.Parameters.AddWithValue("@TableName", tableName);
                command.Parameters.AddWithValue("@SchemaName", schemaName ?? "dbo");

                using var reader = await command.ExecuteReaderAsync();
                
                var columns = new List<Dictionary<string, object>>();
                while (await reader.ReadAsync())
                {
                    columns.Add(new Dictionary<string, object>
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
                        ["is_primary_key"] = Convert.ToBoolean(reader["is_primary_key"])
                    });
                }

                if (!columns.Any())
                {
                    throw new ArgumentException($"Table '{schemaName}.{tableName}' not found or has no columns");
                }

                var finalClassName = className ?? ToPascalCase(tableName);
                var generatedCode = GenerateCSharpModel(columns, finalClassName, @namespace, includeValidation, includeAnnotations);

                var payload = new
                {
                    server_name = SqlConnectionManager.ServerName,
                    environment = SqlConnectionManager.Environment,
                    database = SqlConnectionManager.CurrentDatabase,
                    generated_code = generatedCode,
                    metadata = new
                    {

                        table_name = tableName,
                        schema_name = schemaName,
                        class_name = finalClassName,
                        language = "CSharp",
                        @namespace = @namespace,
                        column_count = columns.Count,
                        generated_at = DateTime.UtcNow
                    },
                    parameters = new
                    {
                        class_name = className,
                        @namespace = @namespace,
                        include_validation = includeValidation,
                        include_annotations = includeAnnotations
                    }
                };
                sw.Stop();
                LoggingHelper.LogEnd(corr, "GenerateModelClass", true, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(payload);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromSqlException(sqlEx, "GenerateModelClass");
                LoggingHelper.LogEnd(Guid.Empty, "GenerateModelClass", false, sw.ElapsedMilliseconds, sqlEx.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var context = ErrorHelper.CreateErrorContextFromException(ex, "GenerateModelClass");
                LoggingHelper.LogEnd(Guid.Empty, "GenerateModelClass", false, sw.ElapsedMilliseconds, ex.Message);
                var response = ResponseFormatter.CreateErrorContextResponse(context, sw.ElapsedMilliseconds);
                return ResponseFormatter.ToJson(response);
            }
        }

        private static string GenerateCSharpModel(List<Dictionary<string, object>> columns, string className, string @namespace, bool includeValidation, bool includeAnnotations)
        {
            var sb = new StringBuilder();
            
            // Add using statements for .NET
            sb.AppendLine("using System;");
            sb.AppendLine("using System.ComponentModel.DataAnnotations;");
            if (includeAnnotations)
            {
                sb.AppendLine("using System.ComponentModel.DataAnnotations.Schema;");
            }
            sb.AppendLine();
            sb.AppendLine("#nullable enable");
            sb.AppendLine();

            // Add namespace and class
            sb.AppendLine($"namespace {@namespace}");
            sb.AppendLine("{");
            if (includeAnnotations)
                sb.AppendLine($"    [Table(\"{className}\")]");
            sb.AppendLine($"    public class {className}");
            sb.AppendLine("    {");

            foreach (var column in columns)
            {
                var columnName = column["column_name"].ToString()!;
                var dataType = column["data_type"].ToString()!;
                var isNullable = Convert.ToBoolean(column["is_nullable"]);
                var isIdentity = Convert.ToBoolean(column["is_identity"]);
                var isComputed = Convert.ToBoolean(column["is_computed"]);
                var description = column["description"]?.ToString();
                var isPrimaryKey = Convert.ToBoolean(column["is_primary_key"]);

                // Add documentation
                if (includeAnnotations && !string.IsNullOrEmpty(description))
                {
                    sb.AppendLine($"        /// <summary>");
                    sb.AppendLine($"        /// {description}");
                    sb.AppendLine($"        /// </summary>");
                }

                // Add attributes
                if (includeAnnotations)
                {
                    sb.AppendLine($"        [Column(\"{columnName}\")]");
                    if (isPrimaryKey)
                        sb.AppendLine($"        [Key]");
                }

                if (includeValidation && !isNullable && !isIdentity)
                {
                    sb.AppendLine($"        [Required]");
                }

                // Add property with proper initialization for reference types
                var propertyType = GetCSharpType(dataType, isNullable, isIdentity);
                var propertyName = ToPascalCase(columnName);
                
                // Add null-forgiving operator for non-nullable reference types that aren't initialized
                var isReferenceType = propertyType == "string" || propertyType == "byte[]" || propertyType == "object";
                var needsDefaultValue = isReferenceType && !isNullable;
                
                if (needsDefaultValue)
                {
                    sb.AppendLine($"        public {propertyType} {propertyName} {{ get; set; }} = default!;");
                }
                else
                {
                    sb.AppendLine($"        public {propertyType} {propertyName} {{ get; set; }}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static string GetCSharpType(string sqlType, bool isNullable, bool isIdentity)
        {
            var sqlTypeLower = sqlType.ToLowerInvariant();
            string baseType;
            bool isReferenceType = false;

            switch (sqlTypeLower)
            {
                case "bit":
                    baseType = "bool";
                    break;
                case "tinyint":
                    baseType = "byte";
                    break;
                case "smallint":
                    baseType = "short";
                    break;
                case "int":
                    baseType = "int";
                    break;
                case "bigint":
                    baseType = "long";
                    break;
                case "decimal":
                case "numeric":
                case "money":
                case "smallmoney":
                    baseType = "decimal";
                    break;
                case "float":
                    baseType = "double";
                    break;
                case "real":
                    baseType = "float";
                    break;
                case "char":
                case "varchar":
                case "nchar":
                case "nvarchar":
                case "text":
                case "ntext":
                case "xml":
                    baseType = "string";
                    isReferenceType = true;
                    break;
                case "datetime":
                case "datetime2":
                case "smalldatetime":
                case "date":
                    baseType = "DateTime";
                    break;
                case "time":
                    baseType = "TimeSpan";
                    break;
                case "datetimeoffset":
                    baseType = "DateTimeOffset";
                    break;
                case "uniqueidentifier":
                    baseType = "Guid";
                    break;
                case "varbinary":
                case "binary":
                case "image":
                case "timestamp":
                case "rowversion":
                    baseType = "byte[]";
                    isReferenceType = true;
                    break;
                default:
                    baseType = "object";
                    isReferenceType = true;
                    break;
            }

            // Value types (not reference types) need nullable modifier when nullable
            // Reference types (string, byte[], object) are already nullable by default in C#
            if (!isReferenceType && isNullable && !isIdentity)
            {
                return baseType + "?";
            }

            return baseType;
        }

        private static string ToPascalCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var words = input.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(words.Select(word => 
                char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant()));
        }
    }
}
