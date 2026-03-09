using ModelContextProtocol.Server;
using SqlServerMcpServer;
using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Xunit;

namespace SqlServerMcpServer.Tests
{
    public class McpToolMetadataTests
    {
        [Fact]
        public void SqlServerTools_Class_ShouldBeMarkedAsMcpServerToolType()
        {
            var attribute = typeof(SqlServerTools).GetCustomAttribute<McpServerToolTypeAttribute>();

            Assert.NotNull(attribute);
        }

        [Fact]
        public void SqlServerTools_ToolMethods_ShouldHaveNonEmptyDescriptions()
        {
            var methods = GetToolMethods();

            Assert.NotEmpty(methods);

            foreach (var method in methods)
            {
                var description = method.GetCustomAttribute<DescriptionAttribute>();

                Assert.NotNull(description);
                Assert.False(string.IsNullOrWhiteSpace(description!.Description), $"Tool '{method.Name}' is missing a non-empty Description attribute.");
            }
        }

        [Fact]
        public void SqlServerTools_ToolParameters_ShouldHaveNonEmptyDescriptions()
        {
            var methods = GetToolMethods();

            foreach (var method in methods)
            {
                foreach (var parameter in method.GetParameters())
                {
                    var description = parameter.GetCustomAttribute<DescriptionAttribute>();

                    Assert.NotNull(description);
                    Assert.False(string.IsNullOrWhiteSpace(description!.Description), $"Parameter '{parameter.Name}' on tool '{method.Name}' is missing a non-empty Description attribute.");
                }
            }
        }

        private static MethodInfo[] GetToolMethods()
        {
            return typeof(SqlServerTools)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
                .ToArray();
        }
    }
}
