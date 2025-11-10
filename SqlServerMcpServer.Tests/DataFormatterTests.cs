using System;
using System.Collections.Generic;
using Xunit;
using SqlServerMcpServer.Utilities;

namespace SqlServerMcpServer.Tests
{
    public class DataFormatterTests
    {
        [Fact]
        public void ParseDelimiter_WithNull_ReturnsComma()
        {
            // Arrange
            string? delimiter = null;

            // Act
            var result = DataFormatter.ParseDelimiter(delimiter);

            // Assert
            Assert.Equal(',', result);
        }

        [Fact]
        public void ParseDelimiter_WithEmptyString_ReturnsComma()
        {
            // Arrange
            string delimiter = "";

            // Act
            var result = DataFormatter.ParseDelimiter(delimiter);

            // Assert
            Assert.Equal(',', result);
        }

        [Fact]
        public void ParseDelimiter_WithTabString_ReturnsTab()
        {
            // Arrange
            string delimiter = "tab";

            // Act
            var result = DataFormatter.ParseDelimiter(delimiter);

            // Assert
            Assert.Equal('\t', result);
        }

        [Fact]
        public void ParseDelimiter_WithBackslashT_ReturnsTab()
        {
            // Arrange
            string delimiter = "\\t";

            // Act
            var result = DataFormatter.ParseDelimiter(delimiter);

            // Assert
            Assert.Equal('\t', result);
        }

        [Fact]
        public void ParseDelimiter_WithSingleChar_ReturnsChar()
        {
            // Arrange
            string delimiter = ";";

            // Act
            var result = DataFormatter.ParseDelimiter(delimiter);

            // Assert
            Assert.Equal(';', result);
        }

        [Fact]
        public void ToCsv_WithBasicData_ReturnsCorrectCsv()
        {
            // Arrange
            var rows = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "Id", 1 },
                    { "Name", "John" },
                    { "Age", 30 }
                },
                new Dictionary<string, object>
                {
                    { "Id", 2 },
                    { "Name", "Jane" },
                    { "Age", 25 }
                }
            };
            var columns = new List<string> { "Id", "Name", "Age" };

            // Act
            var result = DataFormatter.ToCsv(rows, columns, ',');

            // Assert
            var expected = "Id,Name,Age\r\n1,John,30\r\n2,Jane,25\r\n";
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ToCsv_WithSpecialCharacters_ReturnsEscapedCsv()
        {
            // Arrange
            var rows = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "Id", 1 },
                    { "Description", "Contains \"quotes\" and, comma" }
                }
            };
            var columns = new List<string> { "Id", "Description" };

            // Act
            var result = DataFormatter.ToCsv(rows, columns, ',');

            // Assert
            var expected = "Id,Description\r\n1,\"Contains \"\"quotes\"\" and, comma\"\r\n";
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ToHtmlTable_WithBasicData_ReturnsCorrectHtml()
        {
            // Arrange
            var rows = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "Id", 1 },
                    { "Name", "John" },
                    { "Age", 30 }
                },
                new Dictionary<string, object>
                {
                    { "Id", 2 },
                    { "Name", "Jane" },
                    { "Age", 25 }
                }
            };
            var columns = new List<string> { "Id", "Name", "Age" };

            // Act
            var result = DataFormatter.ToHtmlTable(rows, columns);

            // Assert
            Assert.Contains("<table>", result);
            Assert.Contains("<thead><tr>", result);
            Assert.Contains("<th>Id</th>", result);
            Assert.Contains("<th>Name</th>", result);
            Assert.Contains("<th>Age</th>", result);
            Assert.Contains("<tbody>", result);
            Assert.Contains("<td>1</td>", result);
            Assert.Contains("<td>John</td>", result);
            Assert.Contains("<td>30</td>", result);
            Assert.Contains("<td>2</td>", result);
            Assert.Contains("<td>Jane</td>", result);
            Assert.Contains("<td>25</td>", result);
            Assert.Contains("</tbody></table>", result);
        }

        [Fact]
        public void ToHtmlTable_WithSpecialCharacters_ReturnsEscapedHtml()
        {
            // Arrange
            var rows = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "Id", 1 },
                    { "Content", "Contains <script> & \"quotes\"" }
                }
            };
            var columns = new List<string> { "Id", "Content" };

            // Act
            var result = DataFormatter.ToHtmlTable(rows, columns);

            // Assert
            Assert.Contains("&lt;script&gt;", result);
            Assert.Contains("&", result);
            Assert.Contains("&quot;", result);
        }

        [Fact]
        public void ToHtmlTable_WithNullValue_ReturnsNullString()
        {
            // Arrange
            var rows = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "Id", 1 },
                    { "Name", (string?)null }
                }
            };
            var columns = new List<string> { "Id", "Name" };

            // Act
            var result = DataFormatter.ToHtmlTable(rows, columns);

            // Assert
            Assert.Contains("<td>NULL</td>", result);
        }
    }
}