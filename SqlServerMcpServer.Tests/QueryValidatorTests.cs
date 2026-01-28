using System;
using System.Collections.Generic;
using Xunit;
using SqlServerMcpServer.Security;

namespace SqlServerMcpServer.Tests
{
    public class QueryValidatorTests
    {
        #region ClassifyQuery Tests

        [Fact]
        public void ClassifyQuery_WithValidSelect_ReturnsReadOnly()
        {
            // Arrange
            var query = "SELECT * FROM Users";

            // Act
            var result = QueryValidator.ClassifyQuery(query);

            // Assert
            Assert.Equal(QueryType.ReadOnly, result);
        }

        [Fact]
        public void ClassifyQuery_WithSelectWithWhere_ReturnsReadOnly()
        {
            // Arrange
            var query = "SELECT Id, Name FROM Users WHERE Active = 1";

            // Act
            var result = QueryValidator.ClassifyQuery(query);

            // Assert
            Assert.Equal(QueryType.ReadOnly, result);
        }

        [Fact]
        public void ClassifyQuery_WithSelectWithJoin_ReturnsReadOnly()
        {
            // Arrange
            var query = "SELECT u.Name, r.RoleName FROM Users u JOIN Roles r ON u.RoleId = r.Id";

            // Act
            var result = QueryValidator.ClassifyQuery(query);

            // Assert
            Assert.Equal(QueryType.ReadOnly, result);
        }

        [Fact]
        public void ClassifyQuery_WithCTE_ReturnsReadOnly()
        {
            // Arrange
            var query = "WITH ActiveUsers AS (SELECT * FROM Users WHERE Active = 1) SELECT * FROM ActiveUsers";

            // Act
            var result = QueryValidator.ClassifyQuery(query);

            // Assert
            Assert.Equal(QueryType.ReadOnly, result);
        }

        [Fact]
        public void ClassifyQuery_WithInsert_ReturnsInsert()
        {
            // Arrange
            var query = "INSERT INTO Users (Name, Email) VALUES ('John', 'john@example.com')";

            // Act
            var result = QueryValidator.ClassifyQuery(query);

            // Assert
            Assert.Equal(QueryType.Insert, result);
        }

        [Fact]
        public void ClassifyQuery_WithUpdate_ReturnsUpdate()
        {
            // Arrange
            var query = "UPDATE Users SET Name = 'Jane' WHERE Id = 1";

            // Act
            var result = QueryValidator.ClassifyQuery(query);

            // Assert
            Assert.Equal(QueryType.Update, result);
        }

        [Fact]
        public void ClassifyQuery_WithDelete_ReturnsDelete()
        {
            // Arrange
            var query = "DELETE FROM Users WHERE Id = 1";

            // Act
            var result = QueryValidator.ClassifyQuery(query);

            // Assert
            Assert.Equal(QueryType.Delete, result);
        }

        [Fact]
        public void ClassifyQuery_WithTruncate_ReturnsTruncate()
        {
            // Arrange
            var query = "TRUNCATE TABLE Users";

            // Act
            var result = QueryValidator.ClassifyQuery(query);

            // Assert
            Assert.Equal(QueryType.Truncate, result);
        }

        [Fact]
        public void ClassifyQuery_WithDrop_ReturnsDangerous()
        {
            // Arrange
            var query = "DROP TABLE Users";

            // Act
            var result = QueryValidator.ClassifyQuery(query);

            // Assert
            Assert.Equal(QueryType.Dangerous, result);
        }

        [Fact]
        public void ClassifyQuery_WithCreate_ReturnsDangerous()
        {
            // Arrange
            var query = "CREATE TABLE NewTable (Id INT, Name VARCHAR(50))";

            // Act
            var result = QueryValidator.ClassifyQuery(query);

            // Assert
            Assert.Equal(QueryType.Dangerous, result);
        }

        [Fact]
        public void ClassifyQuery_WithExec_ReturnsDangerous()
        {
            // Arrange
            var query = "EXEC sp_help Users";

            // Act
            var result = QueryValidator.ClassifyQuery(query);

            // Assert
            Assert.Equal(QueryType.Dangerous, result);
        }

        #endregion

        #region RequiresConfirmation Tests

        [Fact]
        public void RequiresConfirmation_WithSelect_ReturnsFalse()
        {
            // Arrange
            var query = "SELECT * FROM Users";

            // Act
            var result = QueryValidator.RequiresConfirmation(query);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void RequiresConfirmation_WithInsert_ReturnsFalse()
        {
            // Arrange
            var query = "INSERT INTO Users (Name) VALUES ('John')";

            // Act
            var result = QueryValidator.RequiresConfirmation(query);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void RequiresConfirmation_WithUpdate_ReturnsFalse()
        {
            // Arrange
            var query = "UPDATE Users SET Name = 'Jane' WHERE Id = 1";

            // Act
            var result = QueryValidator.RequiresConfirmation(query);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void RequiresConfirmation_WithDelete_ReturnsTrue()
        {
            // Arrange
            var query = "DELETE FROM Users WHERE Id = 1";

            // Act
            var result = QueryValidator.RequiresConfirmation(query);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void RequiresConfirmation_WithTruncate_ReturnsTrue()
        {
            // Arrange
            var query = "TRUNCATE TABLE Users";

            // Act
            var result = QueryValidator.RequiresConfirmation(query);

            // Assert
            Assert.True(result);
        }

        #endregion

        #region IsDmlQueryAllowed Tests

        [Fact]
        public void IsDmlQueryAllowed_WithSelect_NoConfirmation_ReturnsTrue()
        {
            // Arrange
            var query = "SELECT * FROM Users";

            // Act
            var result = QueryValidator.IsDmlQueryAllowed(query, false, out string? blockedOperation);

            // Assert
            Assert.True(result);
            Assert.Null(blockedOperation);
        }

        [Fact]
        public void IsDmlQueryAllowed_WithInsert_NoConfirmation_ReturnsTrue()
        {
            // Arrange
            var query = "INSERT INTO Users (Name, Email) VALUES ('John', 'john@example.com')";

            // Act
            var result = QueryValidator.IsDmlQueryAllowed(query, false, out string? blockedOperation);

            // Assert
            Assert.True(result);
            Assert.Null(blockedOperation);
        }

        [Fact]
        public void IsDmlQueryAllowed_WithUpdate_NoConfirmation_ReturnsTrue()
        {
            // Arrange
            var query = "UPDATE Users SET Name = 'Jane' WHERE Id = 1";

            // Act
            var result = QueryValidator.IsDmlQueryAllowed(query, false, out string? blockedOperation);

            // Assert
            Assert.True(result);
            Assert.Null(blockedOperation);
        }

        [Fact]
        public void IsDmlQueryAllowed_WithDelete_NoConfirmation_ReturnsFalse()
        {
            // Arrange
            var query = "DELETE FROM Users WHERE Id = 1";

            // Act
            var result = QueryValidator.IsDmlQueryAllowed(query, false, out string? blockedOperation);

            // Assert
            Assert.False(result);
            Assert.Equal("DELETE", blockedOperation);
        }

        [Fact]
        public void IsDmlQueryAllowed_WithDelete_WithConfirmation_ReturnsTrue()
        {
            // Arrange
            var query = "DELETE FROM Users WHERE Id = 1";

            // Act
            var result = QueryValidator.IsDmlQueryAllowed(query, true, out string? blockedOperation);

            // Assert
            Assert.True(result);
            Assert.Null(blockedOperation);
        }

        [Fact]
        public void IsDmlQueryAllowed_WithTruncate_NoConfirmation_ReturnsFalse()
        {
            // Arrange
            var query = "TRUNCATE TABLE Users";

            // Act
            var result = QueryValidator.IsDmlQueryAllowed(query, false, out string? blockedOperation);

            // Assert
            Assert.False(result);
            Assert.Equal("TRUNCATE", blockedOperation);
        }

        [Fact]
        public void IsDmlQueryAllowed_WithTruncate_WithConfirmation_ReturnsTrue()
        {
            // Arrange
            var query = "TRUNCATE TABLE Users";

            // Act
            var result = QueryValidator.IsDmlQueryAllowed(query, true, out string? blockedOperation);

            // Assert
            Assert.True(result);
            Assert.Null(blockedOperation);
        }

        [Fact]
        public void IsDmlQueryAllowed_WithDrop_NoConfirmation_ReturnsFalse()
        {
            // Arrange
            var query = "DROP TABLE Users";

            // Act
            var result = QueryValidator.IsDmlQueryAllowed(query, false, out string? blockedOperation);

            // Assert
            Assert.False(result);
            Assert.Equal("DANGEROUS", blockedOperation);
        }

        [Fact]
        public void IsDmlQueryAllowed_WithDrop_WithConfirmation_ReturnsFalse()
        {
            // Arrange
            var query = "DROP TABLE Users";

            // Act
            var result = QueryValidator.IsDmlQueryAllowed(query, true, out string? blockedOperation);

            // Assert
            Assert.False(result);
            Assert.Equal("DANGEROUS", blockedOperation);
        }

        [Fact]
        public void IsDmlQueryAllowed_WithMultipleStatements_ReturnsFalse()
        {
            // Arrange
            var query = "SELECT * FROM Users; SELECT * FROM Roles;";

            // Act
            var result = QueryValidator.IsDmlQueryAllowed(query, false, out string? blockedOperation);

            // Assert
            Assert.False(result);
            Assert.Equal("MULTIPLE_STATEMENTS", blockedOperation);
        }

        [Fact]
        public void IsDmlQueryAllowed_WithSelectInto_ReturnsFalse()
        {
            // Arrange
            var query = "SELECT * INTO NewTable FROM Users";

            // Act
            var result = QueryValidator.IsDmlQueryAllowed(query, false, out string? blockedOperation);

            // Assert
            Assert.False(result);
            Assert.Equal("DANGEROUS", blockedOperation);
        }

        [Fact]
        public void IsDmlQueryAllowed_WithSingleTrailingSemicolon_ReturnsTrue()
        {
            // Arrange
            var query = "SELECT * FROM Users;";

            // Act
            var result = QueryValidator.IsDmlQueryAllowed(query, false, out string? blockedOperation);

            // Assert
            Assert.True(result);
            Assert.Null(blockedOperation);
        }

        [Fact]
        public void IsDmlQueryAllowed_WithComments_ReturnsCorrectResult()
        {
            // Arrange
            var query = "/* This is a comment */ SELECT * FROM Users -- Another comment";

            // Act
            var result = QueryValidator.IsDmlQueryAllowed(query, false, out string? blockedOperation);

            // Assert
            Assert.True(result);
            Assert.Null(blockedOperation);
        }

        #endregion

        #region IsReadOnlyQuery Tests (Legacy - should still block all DML)

        [Fact]
        public void IsReadOnlyQuery_WithValidSelect_ReturnsTrue()
        {
            // Arrange
            var query = "SELECT * FROM Users";

            // Act
            var result = QueryValidator.IsReadOnlyQuery(query, out string? blockedOperation);

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
            var result = QueryValidator.IsReadOnlyQuery(query, out string? blockedOperation);

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
            var result = QueryValidator.IsReadOnlyQuery(query, out string? blockedOperation);

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
            var result = QueryValidator.IsReadOnlyQuery(query, out string? blockedOperation);

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
            var result = QueryValidator.IsReadOnlyQuery(query, out string? blockedOperation);

            // Assert
            Assert.False(result);
            Assert.Equal("DELETE", blockedOperation);
        }

        #endregion

        #region GetBlockedOperationMessage Tests

        [Fact]
        public void GetBlockedOperationMessage_WithInsert_ReturnsCorrectMessage()
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
        public void GetBlockedOperationMessage_WithDelete_WithoutConfirmation_ReturnsCorrectMessage()
        {
            // Arrange
            var operation = "DELETE";

            // Act
            var message = QueryValidator.GetBlockedOperationMessage(operation, requiresConfirmation: false);

            // Assert
            Assert.Contains("DELETE operations are not allowed", message);
            Assert.Contains("READ-ONLY", message);
        }

        [Fact]
        public void GetBlockedOperationMessage_WithDelete_WithConfirmation_ReturnsCorrectMessage()
        {
            // Arrange
            var operation = "DELETE";

            // Act
            var message = QueryValidator.GetBlockedOperationMessage(operation, requiresConfirmation: true);

            // Assert
            Assert.Contains("DELETE operations require user confirmation", message);
            Assert.Contains("confirm_unsafe_operation", message);
        }

        [Fact]
        public void GetBlockedOperationMessage_WithTruncate_WithoutConfirmation_ReturnsCorrectMessage()
        {
            // Arrange
            var operation = "TRUNCATE";

            // Act
            var message = QueryValidator.GetBlockedOperationMessage(operation, requiresConfirmation: false);

            // Assert
            Assert.Contains("TRUNCATE operations are not allowed", message);
            Assert.Contains("READ-ONLY", message);
        }

        [Fact]
        public void GetBlockedOperationMessage_WithTruncate_WithConfirmation_ReturnsCorrectMessage()
        {
            // Arrange
            var operation = "TRUNCATE";

            // Act
            var message = QueryValidator.GetBlockedOperationMessage(operation, requiresConfirmation: true);

            // Assert
            Assert.Contains("TRUNCATE operations require user confirmation", message);
            Assert.Contains("confirm_unsafe_operation", message);
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

        #endregion

        #region GenerateQueryWarnings Tests

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
            Assert.Contains(warnings, w => w.Contains("may return large result set"));
            Assert.Contains(warnings, w => w.Contains("manual pagination"));
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

        [Fact]
        public void GenerateQueryWarnings_WithInsert_ReturnsModificationWarning()
        {
            // Arrange
            var query = "INSERT INTO Users (Name) VALUES ('John')";

            // Act
            var warnings = QueryValidator.GenerateQueryWarnings(query);

            // Assert
            Assert.Contains(warnings, w => w.Contains("modify data"));
        }

        [Fact]
        public void GenerateQueryWarnings_WithUpdate_ReturnsModificationWarning()
        {
            // Arrange
            var query = "UPDATE Users SET Name = 'Jane' WHERE Id = 1";

            // Act
            var warnings = QueryValidator.GenerateQueryWarnings(query);

            // Assert
            Assert.Contains(warnings, w => w.Contains("modify data"));
        }

        [Fact]
        public void GenerateQueryWarnings_WithDelete_ReturnsDeletionWarning()
        {
            // Arrange
            var query = "DELETE FROM Users WHERE Id = 1";

            // Act
            var warnings = QueryValidator.GenerateQueryWarnings(query);

            // Assert
            Assert.Contains(warnings, w => w.Contains("permanently delete data"));
        }

        [Fact]
        public void GenerateQueryWarnings_WithTruncate_ReturnsDeletionWarning()
        {
            // Arrange
            var query = "TRUNCATE TABLE Users";

            // Act
            var warnings = QueryValidator.GenerateQueryWarnings(query);

            // Assert
            Assert.Contains(warnings, w => w.Contains("permanently delete data"));
        }

        #endregion
    }
}
