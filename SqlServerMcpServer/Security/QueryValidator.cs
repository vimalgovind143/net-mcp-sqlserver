using System.Text.RegularExpressions;

namespace SqlServerMcpServer.Security
{
    /// <summary>
    /// Classification of SQL query types
    /// </summary>
    public enum QueryType
    {
        ReadOnly,      // SELECT queries
        Insert,        // INSERT operations
        Update,        // UPDATE operations
        Delete,        // DELETE operations (requires confirmation)
        Truncate,      // TRUNCATE operations (requires confirmation)
        Dangerous      // DROP, CREATE, ALTER, etc. (always blocked)
    }

    /// <summary>
    /// Validates SQL queries to ensure they are read-only and safe
    /// </summary>
    public static class QueryValidator
    {
        /// <summary>
        /// Classifies a SQL query into its type category
        /// </summary>
        /// <param name="query">The SQL query to classify</param>
        /// <returns>The QueryType classification</returns>
        public static QueryType ClassifyQuery(string query)
        {
            // Remove block and line comments for safer parsing
            var withoutBlockComments = Regex.Replace(query, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            var withoutLineComments = Regex.Replace(withoutBlockComments, @"--.*?$", string.Empty, RegexOptions.Multiline);

            // Normalize whitespace and case
            var normalizedQuery = Regex.Replace(withoutLineComments, @"\s+", " ").Trim();
            var upper = normalizedQuery.ToUpperInvariant();

            // Check for TRUNCATE first (more specific)
            if (Regex.IsMatch(upper, @"\bTRUNCATE\b"))
                return QueryType.Truncate;

            // Check for DELETE
            if (Regex.IsMatch(upper, @"\bDELETE\b"))
                return QueryType.Delete;

            // Check for INSERT
            if (Regex.IsMatch(upper, @"\bINSERT\b"))
                return QueryType.Insert;

            // Check for UPDATE
            if (Regex.IsMatch(upper, @"\bUPDATE\b"))
                return QueryType.Update;

            // Check for dangerous DDL operations
            var dangerousDdlKeywords = new[] { "DROP", "CREATE", "ALTER", "EXEC", "EXECUTE", "MERGE", "BULK", "GRANT", "REVOKE", "DENY" };
            foreach (var keyword in dangerousDdlKeywords)
            {
                if (Regex.IsMatch(upper, $@"\b{keyword}\b"))
                    return QueryType.Dangerous;
            }

            // Allow CTEs starting with WITH provided there's a SELECT
            if (upper.StartsWith("WITH"))
            {
                if (Regex.IsMatch(upper, @"\bSELECT\b"))
                    return QueryType.ReadOnly;
            }
            else if (upper.StartsWith("SELECT"))
            {
                // Block SELECT INTO (creates objects)
                if (Regex.IsMatch(upper, @"\bSELECT\b.*\bINTO\b"))
                    return QueryType.Dangerous;

                return QueryType.ReadOnly;
            }

            return QueryType.Dangerous;
        }

        /// <summary>
        /// Checks if a query requires user confirmation before execution
        /// </summary>
        /// <param name="query">The SQL query to check</param>
        /// <returns>True if confirmation is required</returns>
        public static bool RequiresConfirmation(string query)
        {
            var queryType = ClassifyQuery(query);
            return queryType == QueryType.Delete || queryType == QueryType.Truncate;
        }

        /// <summary>
        /// Checks if a DML query is allowed with optional confirmation
        /// </summary>
        /// <param name="query">The SQL query to validate</param>
        /// <param name="confirmUnsafeOperation">User confirmation for dangerous operations</param>
        /// <param name="blockedOperation">Output parameter for the blocked operation type</param>
        /// <returns>True if the query is allowed to execute</returns>
        public static bool IsDmlQueryAllowed(string query, bool confirmUnsafeOperation, out string? blockedOperation)
        {
            blockedOperation = null;

            // Remove block and line comments for safer parsing
            var withoutBlockComments = Regex.Replace(query, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            var withoutLineComments = Regex.Replace(withoutBlockComments, @"--.*?$", string.Empty, RegexOptions.Multiline);

            // Normalize whitespace and case
            var normalizedQuery = Regex.Replace(withoutLineComments, @"\s+", " ").Trim();
            var upper = normalizedQuery.ToUpperInvariant();

            // Block multiple statements; allow a single trailing semicolon
            if (upper.Contains(";"))
            {
                var trimmedUpper = upper.Trim();
                var first = trimmedUpper.IndexOf(';');
                var last = trimmedUpper.LastIndexOf(';');
                var endsWithSemicolon = last == trimmedUpper.Length - 1;
                var multipleSemicolons = first != last;
                if (!endsWithSemicolon || multipleSemicolons)
                {
                    blockedOperation = "MULTIPLE_STATEMENTS";
                    return false;
                }
            }

            var queryType = ClassifyQuery(query);

            switch (queryType)
            {
                case QueryType.ReadOnly:
                case QueryType.Insert:
                case QueryType.Update:
                    // These are always allowed
                    return true;

                case QueryType.Delete:
                    // Requires confirmation
                    if (!confirmUnsafeOperation)
                    {
                        blockedOperation = "DELETE";
                        return false;
                    }
                    return true;

                case QueryType.Truncate:
                    // Requires confirmation
                    if (!confirmUnsafeOperation)
                    {
                        blockedOperation = "TRUNCATE";
                        return false;
                    }
                    return true;

                case QueryType.Dangerous:
                default:
                    // Block all dangerous operations
                    blockedOperation = "DANGEROUS";
                    return false;
            }
        }

        /// <summary>
        /// Validates if a query is read-only and safe to execute
        /// </summary>
        /// <param name="query">The SQL query to validate</param>
        /// <param name="blockedOperation">Output parameter for the blocked operation type</param>
        /// <returns>True if the query is safe (read-only), false otherwise</returns>
        public static bool IsReadOnlyQuery(string query, out string? blockedOperation)
        {
            blockedOperation = null;

            // Remove block and line comments for safer parsing
            var withoutBlockComments = Regex.Replace(query, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            var withoutLineComments = Regex.Replace(withoutBlockComments, @"--.*?$", string.Empty, RegexOptions.Multiline);

            // Normalize whitespace and case
            var normalizedQuery = Regex.Replace(withoutLineComments, @"\s+", " ").Trim();
            var upper = normalizedQuery.ToUpperInvariant();

            // Block multiple statements; allow a single trailing semicolon
            if (upper.Contains(";"))
            {
                var trimmedUpper = upper.Trim();
                var first = trimmedUpper.IndexOf(';');
                var last = trimmedUpper.LastIndexOf(';');
                var endsWithSemicolon = last == trimmedUpper.Length - 1;
                var multipleSemicolons = first != last;
                if (!endsWithSemicolon || multipleSemicolons)
                {
                    blockedOperation = "MULTIPLE_STATEMENTS";
                    return false;
                }
            }

            // Block dangerous operations (INSERT, UPDATE, DELETE, DROP, etc.)
            var dangerousKeywords = new[] {
                "INSERT", "UPDATE", "DELETE", "DROP", "CREATE", "ALTER",
                "TRUNCATE", "EXEC", "EXECUTE", "MERGE", "BULK", "GRANT", "REVOKE", "DENY",
                "USE", "SET", "DBCC", "BACKUP", "RESTORE", "RECONFIGURE", "SP_CONFIGURE"
            };

            foreach (var keyword in dangerousKeywords)
            {
                if (Regex.IsMatch(upper, $@"\b{keyword}\b"))
                {
                    blockedOperation = keyword;
                    return false;
                }
            }

            // Allow CTEs starting with WITH provided there's a SELECT
            if (upper.StartsWith("WITH"))
            {
                if (!Regex.IsMatch(upper, @"\bSELECT\b"))
                {
                    blockedOperation = "NON_SELECT_STATEMENT";
                    return false;
                }
            }
            else if (!upper.StartsWith("SELECT"))
            {
                blockedOperation = "NON_SELECT_STATEMENT";
                return false;
            }

            // Block SELECT INTO (creates objects)
            if (Regex.IsMatch(upper, @"\bSELECT\b.*\bINTO\b"))
            {
                blockedOperation = "SELECT_INTO";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets an error message for a blocked operation
        /// </summary>
        /// <param name="blockedOperation">The type of operation that was blocked</param>
        /// <param name="requiresConfirmation">Whether the operation requires user confirmation</param>
        /// <returns>User-friendly error message</returns>
        public static string GetBlockedOperationMessage(string blockedOperation, bool requiresConfirmation = false)
        {
            return blockedOperation switch
            {
                "INSERT" => "❌ INSERT operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                "UPDATE" => "❌ UPDATE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                "DELETE" => requiresConfirmation 
                    ? "⚠️ DELETE operations require user confirmation. Set confirm_unsafe_operation=true to proceed."
                    : "❌ DELETE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                "TRUNCATE" => requiresConfirmation
                    ? "⚠️ TRUNCATE operations require user confirmation. Set confirm_unsafe_operation=true to proceed."
                    : "❌ TRUNCATE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                "DROP" => "❌ DROP operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                "CREATE" => "❌ CREATE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                "ALTER" => "❌ ALTER operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                "EXEC" or "EXECUTE" => "❌ EXEC/EXECUTE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                "MERGE" => "❌ MERGE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                "BULK" => "❌ BULK operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                "GRANT" => "❌ GRANT operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                "REVOKE" => "❌ REVOKE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                "DENY" => "❌ DENY operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                "SELECT_INTO" => "❌ SELECT INTO is not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                "MULTIPLE_STATEMENTS" => "❌ Multiple statements are not allowed. Submit a single SELECT statement.",
                "NON_SELECT_STATEMENT" => "❌ Only SELECT statements are allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                _ => $"❌ {blockedOperation} operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing."
            };
        }

        /// <summary>
        /// Generates query warnings based on query analysis
        /// </summary>
        /// <param name="query">The SQL query to analyze</param>
        /// <param name="offset">Pagination offset if used</param>
        /// <returns>List of warnings</returns>
        public static List<string> GenerateQueryWarnings(string query, int? offset = null)
        {
            var warnings = new List<string>();
            var upperQuery = query.ToUpperInvariant();
            
            if (!upperQuery.Contains("WHERE") && !upperQuery.Contains("TOP"))
            {
                warnings.Add("Query may return large result set - consider adding WHERE clause or TOP limit");
            }
            
            if (offset.HasValue && offset.Value > 0 && !upperQuery.Contains("OFFSET"))
            {
                warnings.Add("Using manual pagination - consider using OFFSET/FETCH in your query for better performance");
            }

            // Add warning for DML operations
            var queryType = ClassifyQuery(query);
            if (queryType == QueryType.Insert || queryType == QueryType.Update)
            {
                warnings.Add("⚠️ This query will modify data - ensure you have a backup before proceeding");
            }
            else if (queryType == QueryType.Delete || queryType == QueryType.Truncate)
            {
                warnings.Add("⚠️ This query will permanently delete data - ensure you have a backup before proceeding");
            }

            return warnings;
        }
    }
}
