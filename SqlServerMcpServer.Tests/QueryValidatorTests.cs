using System;
using System.Collections.Generic;
using Xunit;
using SqlServerMcpServer.Security;

namespace SqlServerMcpServer.Tests
{
    public class QueryValidatorTests
    {
        [Fact]
        public void IsReadOnlyQuery_WithValidSelect_ReturnsTrue()
        {
            // Arrange
            var query = "SELECT * FROM Users";

            // Act
            var result = QueryValidator.IsReadOnlyQuery(query, out string blockedOperation);

            // Assert
            Assert.True(result);
            Assert.Null(blockedOperation);
        }

        [Fact]
        public void IsReadOnlyQuery_WithSelectWithWhere_ReturnsTrue()
        {
            // Arrange
            var query = "SELECT Id, Name FROM Users WHERE Active = 1";

            // Act
            var result = QueryValidator.IsReadOnlyQuery(query, out string blockedOperation);

            // Assert
            Assert.True(result);
            Assert.Null(blockedOperation);
        }

        [Fact]
        public void IsReadOnlyQuery_WithSelectWithJoin_ReturnsTrue()
        {
            // Arrange
            var query = "SELECT u.Name, r.RoleName FROM Users u JOIN Roles r ON u.RoleId = r.Id";

            // Act
            var result = QueryValidator.IsReadOnlyQuery(query, out string blockedOperation);

            // Assert
            Assert.True(result);
            Assert.Null(blockedOperation);
        }

        [Fact]
        public void IsReadOnlyQuery_WithCTE_ReturnsTrue()
        {
            // Arrange
            var query = "WITH ActiveUsers AS (SELECT * FROM Users WHERE Active = 1) SELECT * FROM ActiveUsers";

            // Act
            var result = QueryValidator.IsReadOnlyQuery(query, out string blockedOperation);

            // Assert
            Assert.True(result);
            Assert.Null(blockedOperation);
        }

        [Fact]
        public void IsReadOnlyQuery_WithInsert_ReturnsFalse()
        {
            // Arrange
            var query = "INSERT INTO Users (Name, Email) VALUES ('John', 'john@example.com')";

            // Act
            var result = QueryValidator.IsReadOnlyQuery(query, out string blockedOperation);

            // Assert
            Assert.False(result);
            Assert.Equal("INSERT", blockedOperation);
        }

        [Fact]
        public void IsReadOnlyQuery_WithUpdate_ReturnsFalse()
        {
            // Arrange
            var query = "UPDATE Users SET Name = 'Jane' WHERE Id = 1";

            // Act
            var result = QueryValidator.IsReadOnlyQuery(query, out string blockedOperation);

            // Assert
            Assert.False(result);
            Assert.Equal("UPDATE", blockedOperation);
        }

        [Fact]
        public void IsReadOnlyQuery_WithDelete_ReturnsFalse()
        {
            // Arrange
            var query = "DELETE FROM Users WHERE Id = 1";

            // Act
            var result = QueryValidator.IsReadOnlyQuery(query, out string blockedOperation);

            // Assert
            Assert.False(result);
            Assert.Equal("DELETE", blockedOperation);
        }

        [Fact]
        public void IsReadOnlyQuery_WithDrop_ReturnsFalse()
        {
            // Arrange
            var query = "DROP TABLE Users";

            // Act
            var result = QueryValidator.IsReadOnlyQuery(query, out string blockedOperation);

            // Assert
            Assert.False(result);
            Assert.Equal("DROP", blockedOperation);
        }

        [Fact]
        public void IsReadOnlyQuery_WithCreate_ReturnsFalse()
        {
            // Arrange
            var query = "CREATE TABLE NewTable (Id INT, Name VARCHAR(50))";

            // Act
            var result = QueryValidator.IsReadOnlyQuery(query, out string blockedOperation);

            // Assert
            Assert.False(result);
            Assert.Equal("CREATE", blockedOperation);
        }

        [Fact]
        public void IsReadOnlyQuery_WithExec_ReturnsFalse()
        {
            // Arrange
            var query = "EXEC sp_help Users";

            // Act
            var result = QueryValidator.IsReadOnlyQuery(query, out string blockedOperation);

            // Assert
            Assert.False(result);
            Assert.Equal("EXEC", blockedOperation);
        }

        [Fact]
        public void IsReadOnlyQuery_WithMultipleStatements_ReturnsFalse()
        {
            // Arrange
            var query = "SELECT * FROM Users; SELECT * FROM Roles;";

            // Act
            var result = QueryValidator.IsReadOnlyQuery(query, out string blockedOperation);

            // Assert
            Assert.False(result);
            Assert.Equal("MULTIPLE_STATEMENTS", blockedOperation);
        }

        [Fact]
        public void IsReadOnlyQuery_WithSelectInto_ReturnsFalse()
        {
            // Arrange
            var query = "SELECT * INTO NewTable FROM Users";

            // Act
            var result = QueryValidator.IsReadOnlyQuery(query, out string blockedOperation);

            // Assert
            Assert.False(result);
            Assert.Equal("SELECT_INTO", blockedOperation);
        }

        [Fact]
        public void IsReadOnlyQuery_WithSingleTrailingSemicolon_ReturnsTrue()
        {
            // Arrange
            var query = "SELECT * FROM Users;";

            // Act
            var result = QueryValidator.IsReadOnlyQuery(query, out string blockedOperation);

            // Assert
            Assert.True(result);
            Assert.Null(blockedOperation);
        }

        [Fact]
        public void IsReadOnlyQuery_WithComments_ReturnsCorrectResult()
        {
            // Arrange
            var query = "/* This is a comment */ SELECT * FROM Users -- Another comment";

            // Act
            var result = QueryValidator.IsReadOnlyQuery(query, out string blockedOperation);

            // Assert
            Assert.True(result);
            Assert.Null(blockedOperation);
        }

        [Fact]
        public void GetBlockedOperationMessage_WithKnownOperation_ReturnsCorrectMessage()
        {
            // Arrange
            var operation = "INSERT";

            // Act
            var message = QueryValidator.GetBlockedOperationMessage(operation);

            // Assert
            Assert.Contains("INSERT operations are not allowed", message);
            Assert.Contains("READ-ONLY", message);
        }

        [Fact]
        public void GetBlockedOperationMessage_WithUnknownOperation_ReturnsGenericMessage()
        {
            // Arrange
            var operation = "UNKNOWN_OPERATION";

            // Act
            var message = QueryValidator.GetBlockedOperationMessage(operation);

            // Assert
            Assert.Contains("UNKNOWN_OPERATION operations are not allowed", message);
            Assert.Contains("READ-ONLY", message);
        }

        [Fact]
        public void GenerateQueryWarnings_WithNoWhereOrTop_ReturnsWarning()
        {
            // Arrange
            var query = "SELECT * FROM Users";

            // Act
            var warnings = QueryValidator.GenerateQueryWarnings(query);

            // Assert
            Assert.Single(warnings);
            Assert.Contains("may return large result set", warnings[0]);
        }

        [Fact]
        public void GenerateQueryWarnings_WithWhere_ReturnsNoWarning()
        {
            // Arrange
            var query = "SELECT * FROM Users WHERE Active = 1";

            // Act
            var warnings = QueryValidator.GenerateQueryWarnings(query);

            // Assert
            Assert.Empty(warnings);
        }

        [Fact]
        public void GenerateQueryWarnings_WithTop_ReturnsNoWarning()
        {
            // Arrange
            var query = "SELECT TOP 10 * FROM Users";

            // Act
            var warnings = QueryValidator.GenerateQueryWarnings(query);

            // Assert
            Assert.Empty(warnings);
        }

        [Fact]
        public void GenerateQueryWarnings_WithOffset_ReturnsWarning()
        {
            // Arrange
            var query = "SELECT * FROM Users";
            var offset = 100;

            // Act
            var warnings = QueryValidator.GenerateQueryWarnings(query, offset);

            // Assert
            Assert.True(warnings.Count >= 1);
            Assert.True(warnings.Any(w => w.Contains("may return large result set")));
            Assert.True(warnings.Any(w => w.Contains("manual pagination")));
        }

        [Fact]
        public void GenerateQueryWarnings_WithOffsetAndOffsetClause_ReturnsNoWarning()
        {
            // Arrange
            var query = "SELECT * FROM Users ORDER BY Id OFFSET 100 ROWS FETCH NEXT 50 ROWS ONLY";
            var offset = 100;

            // Act
            var warnings = QueryValidator.GenerateQueryWarnings(query, offset);

            // Assert
            // Should still warn about large result set since there's no WHERE clause
            Assert.Single(warnings);
            Assert.Contains("may return large result set", warnings[0]);
        }
    }
}