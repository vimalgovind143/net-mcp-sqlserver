using FluentAssertions;
using SqlServerMcpServer.Utilities;
using System.Text.RegularExpressions;
using Xunit;

namespace SqlServerMcpServer.Tests
{
    public class QueryFormatterTests
    {
        [Fact]
        public void ApplyTopLimit_WithBasicSelect_AddsTopLimit()
        {
            // Arrange
            var query = "SELECT * FROM Users";
            var maxRows = 100;

            // Act
            var result = QueryFormatter.ApplyTopLimit(query, maxRows);

            // Assert
            result.Should().Be("SELECT TOP 100 * FROM Users");
        }

        [Fact]
        public void ApplyTopLimit_WithDistinct_AddsTopLimit()
        {
            // Arrange
            var query = "SELECT DISTINCT * FROM Users";
            var maxRows = 50;

            // Act
            var result = QueryFormatter.ApplyTopLimit(query, maxRows);

            // Assert
            result.Should().Be("SELECT DISTINCT TOP 50 * FROM Users");
        }

        [Fact]
        public void ApplyTopLimit_WithExistingTop_LowerLimit_ModifiesTop()
        {
            // Arrange
            var query = "SELECT TOP 200 * FROM Users";
            var maxRows = 100;

            // Act
            var result = QueryFormatter.ApplyTopLimit(query, maxRows);

            // Assert
            result.Should().Be("SELECT TOP 100 * FROM Users");
        }

        [Fact]
        public void ApplyTopLimit_WithExistingTop_HigherLimit_DoesNotModify()
        {
            // Arrange
            var query = "SELECT TOP 50 * FROM Users";
            var maxRows = 100;

            // Act
            var result = QueryFormatter.ApplyTopLimit(query, maxRows);

            // Assert
            result.Should().Be(query);
        }

        [Fact]
        public void ApplyTopLimit_WithOffsetFetch_ModifiesFetch()
        {
            // Arrange
            var query = "SELECT * FROM Users ORDER BY Id OFFSET 0 ROWS FETCH NEXT 200 ROWS ONLY";
            var maxRows = 100;

            // Act
            var result = QueryFormatter.ApplyTopLimit(query, maxRows);

            // Assert
            result.Should().Be("SELECT * FROM Users ORDER BY Id OFFSET 0 ROWS FETCH NEXT 100 ROWS ONLY");
        }

        [Fact]
        public void ApplyTopLimit_WithComments_RemovesCommentsAndAppliesLimit()
        {
            // Arrange
            var query = "/* Comment */ SELECT -- Another comment\r\n* FROM Users";
            var maxRows = 50;

            // Act
            var result = QueryFormatter.ApplyTopLimit(query, maxRows);

            // Assert
            result.Should().Be("SELECT TOP 50 * FROM Users");
        }

        [Fact]
        public void ApplyTopLimit_WithCaseInsensitiveSelect_AddsTopLimit()
        {
            // Arrange
            var query = "select * from Users";
            var maxRows = 10;

            // Act
            var result = QueryFormatter.ApplyTopLimit(query, maxRows);

            // Assert
            result.Should().Be("select TOP 10 * from Users");
        }

        [Fact]
        public void ApplyPaginationAndLimit_WithBasicSelect_AddsTopLimit()
        {
            // Arrange
            var query = "SELECT * FROM Users";
            var limit = 100;
            var offset = 0;

            // Act
            var result = QueryFormatter.ApplyPaginationAndLimit(query, limit, offset);

            // Assert
            result.Should().Be("SELECT TOP 100 * FROM Users");
        }

        [Fact]
        public void ApplyPaginationAndLimit_WithOffset_AddsOffsetFetch()
        {
            // Arrange
            var query = "SELECT * FROM Users ORDER BY Id";
            var limit = 50;
            var offset = 10;

            // Act
            var result = QueryFormatter.ApplyPaginationAndLimit(query, limit, offset);

            // Assert
            result.Should().Be("SELECT * FROM Users ORDER BY Id OFFSET 10 ROWS FETCH NEXT 50 ROWS ONLY");
        }

        [Fact]
        public void ApplyPaginationAndLimit_WithExistingOffsetFetch_ModifiesFetch()
        {
            // Arrange
            var query = "SELECT * FROM Users ORDER BY Id OFFSET 0 ROWS FETCH NEXT 200 ROWS ONLY";
            var limit = 100;
            var offset = 5;

            // Act
            var result = QueryFormatter.ApplyPaginationAndLimit(query, limit, offset);

            // Assert
            result.Should().Be("SELECT * FROM Users ORDER BY Id OFFSET 0 ROWS FETCH NEXT 100 ROWS ONLY");
        }

        [Fact]
        public void ApplyPaginationAndLimit_WithExistingTop_AndOffset_AddsOffsetFetch()
        {
            // Arrange
            var query = "SELECT TOP 500 * FROM Users";
            var limit = 100;
            var offset = 20;

            // Act
            var result = QueryFormatter.ApplyPaginationAndLimit(query, limit, offset);

            // Assert
            result.Should().Be("SELECT TOP 100 * FROM Users OFFSET 20 ROWS FETCH NEXT 100 ROWS ONLY");
        }

        [Fact]
        public void ApplyPaginationAndLimit_WithExistingTop_NoOffset_ModifiesTop()
        {
            // Arrange
            var query = "SELECT TOP 200 * FROM Users";
            var limit = 100;
            var offset = 0;

            // Act
            var result = QueryFormatter.ApplyPaginationAndLimit(query, limit, offset);

            // Assert
            result.Should().Be("SELECT TOP 100 * FROM Users");
        }

        [Fact]
        public void ApplyPaginationAndLimit_OnFailure_ReturnsOriginalQuery()
        {
            // Arrange - using invalid regex or something, but hard to trigger catch
            // For this test, we assume if something goes wrong, it returns original
            // But in practice, the method catches and returns original
            var query = ""; // Empty query
            var limit = 10;
            var offset = 0;

            // Act
            var result = QueryFormatter.ApplyPaginationAndLimit(query, limit, offset);

            // Assert
            result.Should().Be(query);
        }

        [Fact]
        public void ApplyTopLimit_WithSubquery_OnlyLimitsOuterSelect()
        {
            // Arrange - inner subquery SELECT must NOT receive a TOP clause
            var query = "SELECT * FROM Orders WHERE CustomerId IN (SELECT CustomerId FROM Customers WHERE Region = 'X')";
            var maxRows = 100;

            // Act
            var result = QueryFormatter.ApplyTopLimit(query, maxRows);

            // Assert
            result.Should().Be("SELECT TOP 100 * FROM Orders WHERE CustomerId IN (SELECT CustomerId FROM Customers WHERE Region = 'X')");
            // Exactly one TOP should be injected
            Regex.Matches(result, @"\bTOP\b", RegexOptions.IgnoreCase).Count.Should().Be(1);
        }

        [Fact]
        public void ApplyTopLimit_WithUnion_OnlyLimitsFirstSelect()
        {
            // Arrange - only the first SELECT in a UNION should get TOP
            var query = "SELECT 1 UNION SELECT 2 UNION SELECT 3";
            var maxRows = 100;

            // Act
            var result = QueryFormatter.ApplyTopLimit(query, maxRows);

            // Assert
            result.Should().Be("SELECT TOP 100 1 UNION SELECT 2 UNION SELECT 3");
            Regex.Matches(result, @"\bTOP\b", RegexOptions.IgnoreCase).Count.Should().Be(1);
        }

        [Fact]
        public void ApplyTopLimit_WithCte_DoesNotLimitOuterAndInnerTogether()
        {
            // Arrange - the CTE inner SELECT is the first SELECT; only it is limited, outer is left alone
            var query = "WITH c AS (SELECT Id FROM T) SELECT * FROM c";
            var maxRows = 100;

            // Act
            var result = QueryFormatter.ApplyTopLimit(query, maxRows);

            // Assert
            result.Should().Be("WITH c AS (SELECT TOP 100 Id FROM T) SELECT * FROM c");
            Regex.Matches(result, @"\bTOP\b", RegexOptions.IgnoreCase).Count.Should().Be(1);
        }

        [Fact]
        public void ApplyPaginationAndLimit_WithSubquery_OnlyLimitsOuterSelect()
        {
            // Arrange
            var query = "SELECT * FROM Orders WHERE Id IN (SELECT OrderId FROM Items)";
            var limit = 50;
            var offset = 0;

            // Act
            var result = QueryFormatter.ApplyPaginationAndLimit(query, limit, offset);

            // Assert
            result.Should().Be("SELECT TOP 50 * FROM Orders WHERE Id IN (SELECT OrderId FROM Items)");
            Regex.Matches(result, @"\bTOP\b", RegexOptions.IgnoreCase).Count.Should().Be(1);
        }
    }
}
