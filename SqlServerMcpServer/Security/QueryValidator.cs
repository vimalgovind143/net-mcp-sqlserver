using System.Text.RegularExpressions;

namespace SqlServerMcpServer.Security
{
    /// <summary>
    /// Validates SQL queries to ensure they are read-only and safe
    /// </summary>
    public static class QueryValidator
    {
        /// <summary>
        /// Validates if a query is read-only and safe to execute
        /// </summary>
        /// <param name="query">The SQL query to validate</param>
        /// <param name="blockedOperation">Output parameter for the blocked operation type</param>
        /// <returns>True if the query is safe, false otherwise</returns>
        public static bool IsReadOnlyQuery(string query, out string blockedOperation)
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

            // Block dangerous operations and identify what was blocked
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
        /// <returns>User-friendly error message</returns>
        public static string GetBlockedOperationMessage(string blockedOperation)
        {
            return blockedOperation switch
            {
                "INSERT" => "❌ INSERT operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                "UPDATE" => "❌ UPDATE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                "DELETE" => "❌ DELETE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                "DROP" => "❌ DROP operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                "CREATE" => "❌ CREATE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                "ALTER" => "❌ ALTER operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
                "TRUNCATE" => "❌ TRUNCATE operations are not allowed. This MCP server is READ-ONLY and only supports SELECT queries for data viewing.",
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

            return warnings;
        }
    }
}