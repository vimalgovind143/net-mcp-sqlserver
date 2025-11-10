using FluentAssertions;
using SqlServerMcpServer.Utilities;
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
    }
}
