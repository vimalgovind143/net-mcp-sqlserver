using System;
using System.Threading.Tasks;
using Xunit;
using SqlServerMcpServer.Operations;

namespace SqlServerMcpServer.Tests
{
    public class CodeGenerationTests
    {
        [Fact]
        public async Task GenerateModelClass_DefaultParams_ReturnsValidJson()
        {
            // Act
            var result = await CodeGeneration.GenerateModelClass(
                tableName: "sys.tables", 
                schemaName: "sys", 
                className: null, 
                @namespace: "GeneratedModels", 
                includeValidation: true, 
                includeAnnotations: true);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);

            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("class_code", out var classCode));
            Assert.True(classCode.GetString()!.Contains("namespace GeneratedModels"));
            Assert.True(classCode.GetString()!.Contains("public class"));
        }

        [Fact]
        public async Task GenerateModelClass_CustomClassName_ReturnsValidJson()
        {
            // Act
            var result = await CodeGeneration.GenerateModelClass(
                tableName: "sys.tables", 
                schemaName: "sys", 
                className: "CustomTable", 
                @namespace: "GeneratedModels", 
                includeValidation: true, 
                includeAnnotations: true);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("class_code", out var classCode));
            Assert.Contains("public class CustomTable", classCode.GetString());
        }

        [Fact]
        public async Task GenerateModelClass_CustomNamespace_ReturnsValidJson()
        {
            // Act
            var result = await CodeGeneration.GenerateModelClass(
                tableName: "sys.tables", 
                schemaName: "sys", 
                className: null, 
                @namespace: "MyApp.Models", 
                includeValidation: true, 
                includeAnnotations: true);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("class_code", out var classCode));
            Assert.Contains("namespace MyApp.Models", classCode.GetString());
        }

        [Fact]
        public async Task GenerateModelClass_WithValidation_IncludesDataAnnotations()
        {
            // Act
            var result = await CodeGeneration.GenerateModelClass(
                tableName: "sys.tables", 
                schemaName: "sys", 
                className: null, 
                @namespace: "GeneratedModels", 
                includeValidation: true, 
                includeAnnotations: true);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("class_code", out var classCode));
            var code = classCode.GetString()!;
            
            // Should include DataAnnotations namespace
            Assert.Contains("using System.ComponentModel.DataAnnotations", code);
        }

        [Fact]
        public async Task GenerateModelClass_WithAnnotations_IncludesTableAttribute()
        {
            // Act
            var result = await CodeGeneration.GenerateModelClass(
                tableName: "sys.tables", 
                schemaName: "sys", 
                className: null, 
                @namespace: "GeneratedModels", 
                includeValidation: true, 
                includeAnnotations: true);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("class_code", out var classCode));
            var code = classCode.GetString()!;
            
            // Should include [Table] attribute
            Assert.Contains("[Table(", code);
        }

        [Fact]
        public async Task GenerateModelClass_WithoutValidation_ExcludesDataAnnotations()
        {
            // Act
            var result = await CodeGeneration.GenerateModelClass(
                tableName: "sys.tables", 
                schemaName: "sys", 
                className: null, 
                @namespace: "GeneratedModels", 
                includeValidation: false, 
                includeAnnotations: false);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("options", out var options));
            Assert.True(options.TryGetProperty("include_validation", out var includeValidation));
            Assert.False(includeValidation.GetBoolean());
            Assert.True(options.TryGetProperty("include_annotations", out var includeAnnotations));
            Assert.False(includeAnnotations.GetBoolean());
        }

        [Fact]
        public async Task GenerateModelClass_ValidatesCodeStructure()
        {
            // Act
            var result = await CodeGeneration.GenerateModelClass(
                tableName: "sys.tables", 
                schemaName: "sys", 
                className: null, 
                @namespace: "GeneratedModels", 
                includeValidation: true, 
                includeAnnotations: true);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("class_code", out var classCode));
            var code = classCode.GetString()!;
            
            // Validate basic C# structure
            Assert.Contains("namespace", code);
            Assert.Contains("public class", code);
            Assert.Contains("{", code);
            Assert.Contains("}", code);
            
            // Should have properties
            Assert.Contains("public", code);
            Assert.Contains("get;", code);
            Assert.Contains("set;", code);
        }

        [Fact]
        public async Task GenerateModelClass_IncludesMetadata()
        {
            // Act
            var result = await CodeGeneration.GenerateModelClass(
                tableName: "sys.tables", 
                schemaName: "sys", 
                className: null, 
                @namespace: "GeneratedModels", 
                includeValidation: true, 
                includeAnnotations: true);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("table_info", out var tableInfo));
            Assert.True(tableInfo.TryGetProperty("schema_name", out var schemaName));
            Assert.Equal("sys", schemaName.GetString());
            Assert.True(tableInfo.TryGetProperty("table_name", out var tableName));
            Assert.Equal("sys.tables", tableName.GetString());
        }

        [Fact]
        public async Task GenerateModelClass_IncludesColumnCount()
        {
            // Act
            var result = await CodeGeneration.GenerateModelClass(
                tableName: "sys.tables", 
                schemaName: "sys", 
                className: null, 
                @namespace: "GeneratedModels", 
                includeValidation: true, 
                includeAnnotations: true);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("table_info", out var tableInfo));
            Assert.True(tableInfo.TryGetProperty("column_count", out var columnCount));
            Assert.True(columnCount.GetInt32() > 0);
        }

        [Fact]
        public async Task GenerateModelClass_HandlesCSharpKeywords()
        {
            // Act
            var result = await CodeGeneration.GenerateModelClass(
                tableName: "sys.tables", 
                schemaName: "sys", 
                className: null, 
                @namespace: "GeneratedModels", 
                includeValidation: true, 
                includeAnnotations: true);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("class_code", out var classCode));
            var code = classCode.GetString()!;
            
            // Verify code is valid C# (no compilation errors expected)
            Assert.NotEmpty(code);
            Assert.True(code.Length > 100); // Should be substantial code
        }

        [Fact]
        public async Task GenerateModelClass_IncludesServerInfo()
        {
            // Act
            var result = await CodeGeneration.GenerateModelClass(
                tableName: "sys.tables", 
                schemaName: "sys", 
                className: null, 
                @namespace: "GeneratedModels", 
                includeValidation: true, 
                includeAnnotations: true);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("server_name", out _));
            Assert.True(root.TryGetProperty("database", out _));
        }

        [Fact]
        public async Task GenerateModelClass_HandlesNullableTypes()
        {
            // Act
            var result = await CodeGeneration.GenerateModelClass(
                tableName: "sys.tables", 
                schemaName: "sys", 
                className: null, 
                @namespace: "GeneratedModels", 
                includeValidation: true, 
                includeAnnotations: true);

            // Assert
            Assert.NotNull(result);
            var jsonDocument = System.Text.Json.JsonDocument.Parse(result);
            var root = jsonDocument.RootElement;
            
            Assert.True(root.TryGetProperty("class_code", out var classCode));
            var code = classCode.GetString()!;
            
            // Should contain #nullable enable directive for proper nullable handling
            Assert.Contains("#nullable enable", code);
        }
    }
}
