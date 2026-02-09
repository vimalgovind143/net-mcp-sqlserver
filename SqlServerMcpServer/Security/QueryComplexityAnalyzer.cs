using System.Text.RegularExpressions;

namespace SqlServerMcpServer.Security
{
    /// <summary>
    /// Analyzes SQL query complexity to prevent resource exhaustion attacks
    /// </summary>
    public static class QueryComplexityAnalyzer
    {
        // Configuration - can be overridden via environment variables
        private static readonly int _maxComplexityScore = ParseIntEnv("MCP_MAX_QUERY_COMPLEXITY", 1000);
        private static readonly int _maxJoinCount = ParseIntEnv("MCP_MAX_JOIN_COUNT", 10);
        private static readonly int _maxSubqueryDepth = ParseIntEnv("MCP_MAX_SUBQUERY_DEPTH", 3);
        private static readonly int _maxQueryLength = ParseIntEnv("MCP_MAX_QUERY_LENGTH", 100000);
        private static readonly int _maxInClauseItems = ParseIntEnv("MCP_MAX_IN_CLAUSE_ITEMS", 1000);
        private static readonly int _maxUnionCount = ParseIntEnv("MCP_MAX_UNION_COUNT", 5);

        /// <summary>
        /// Complexity score factors
        /// </summary>
        public class ComplexityScore
        {
            public int TotalScore { get; set; }
            public int JoinCount { get; set; }
            public int SubqueryDepth { get; set; }
            public int UnionCount { get; set; }
            public int InClauseCount { get; set; }
            public int FunctionCalls { get; set; }
            public int QueryLength { get; set; }
            public int WildcardCount { get; set; }
            public int DistinctCount { get; set; }
            public int GroupByCount { get; set; }
            public int OrderByCount { get; set; }
            public int WindowFunctionCount { get; set; }
            public List<string> Warnings { get; set; } = new List<string>();
            public bool IsAllowed { get; set; }
            public string? BlockReason { get; set; }
        }

        /// <summary>
        /// Analyzes a query and returns its complexity score
        /// </summary>
        /// <param name="query">The SQL query to analyze</param>
        /// <returns>Complexity score with breakdown</returns>
        public static ComplexityScore Analyze(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new ComplexityScore
                {
                    IsAllowed = false,
                    BlockReason = "Query is empty"
                };
            }

            var score = new ComplexityScore();
            var warnings = new List<string>();

            // Remove comments for analysis
            var cleanQuery = RemoveComments(query);
            var upperQuery = cleanQuery.ToUpperInvariant();

            // Check query length
            score.QueryLength = query.Length;
            if (score.QueryLength > _maxQueryLength)
            {
                score.IsAllowed = false;
                score.BlockReason = $"Query exceeds maximum length of {_maxQueryLength} characters";
                return score;
            }

            // Count JOINs
            score.JoinCount = CountJoins(upperQuery);
            if (score.JoinCount > _maxJoinCount)
            {
                score.IsAllowed = false;
                score.BlockReason = $"Query has {score.JoinCount} JOINs, maximum allowed is {_maxJoinCount}";
                return score;
            }

            // Calculate subquery depth
            score.SubqueryDepth = CalculateSubqueryDepth(cleanQuery);
            if (score.SubqueryDepth > _maxSubqueryDepth)
            {
                score.IsAllowed = false;
                score.BlockReason = $"Query has subquery depth of {score.SubqueryDepth}, maximum allowed is {_maxSubqueryDepth}";
                return score;
            }

            // Count UNION/UNION ALL
            score.UnionCount = CountUnions(upperQuery);
            if (score.UnionCount > _maxUnionCount)
            {
                score.IsAllowed = false;
                score.BlockReason = $"Query has {score.UnionCount} UNIONs, maximum allowed is {_maxUnionCount}";
                return score;
            }

            // Count IN clause items
            score.InClauseCount = CountInClauseItems(upperQuery);
            if (score.InClauseCount > _maxInClauseItems)
            {
                score.IsAllowed = false;
                score.BlockReason = $"Query has {score.InClauseCount} IN clause items, maximum allowed is {_maxInClauseItems}";
                return score;
            }

            // Count function calls
            score.FunctionCalls = CountFunctionCalls(upperQuery);

            // Count wildcards in LIKE patterns
            score.WildcardCount = CountWildcards(upperQuery);

            // Count DISTINCT
            score.DistinctCount = CountDistinct(upperQuery);

            // Count GROUP BY
            score.GroupByCount = CountGroupBy(upperQuery);

            // Count ORDER BY clauses
            score.OrderByCount = CountOrderBy(upperQuery);

            // Count window functions
            score.WindowFunctionCount = CountWindowFunctions(upperQuery);

            // Calculate total complexity score
            score.TotalScore = CalculateTotalScore(score);

            // Generate warnings for high complexity
            if (score.TotalScore > _maxComplexityScore * 0.8)
            {
                warnings.Add($"Query complexity ({score.TotalScore}) is approaching limit ({_maxComplexityScore})");
            }
            if (score.JoinCount > _maxJoinCount * 0.7)
            {
                warnings.Add($"Query has many JOINs ({score.JoinCount}), consider simplifying");
            }
            if (score.WildcardCount > 0 && score.WildcardCount >= score.QueryLength / 100)
            {
                warnings.Add("Query contains many LIKE wildcards, may impact performance");
            }
            if (score.WindowFunctionCount > 5)
            {
                warnings.Add($"Query has {score.WindowFunctionCount} window functions, may be resource intensive");
            }

            score.Warnings = warnings;
            score.IsAllowed = score.TotalScore <= _maxComplexityScore;
            if (!score.IsAllowed)
            {
                score.BlockReason = $"Query complexity score ({score.TotalScore}) exceeds maximum ({_maxComplexityScore})";
            }

            return score;
        }

        /// <summary>
        /// Quickly checks if a query should be blocked without full analysis
        /// </summary>
        /// <param name="query">The SQL query to check</param>
        /// <returns>True if query is allowed</returns>
        public static bool IsQueryAllowed(string query)
        {
            var score = Analyze(query);
            return score.IsAllowed;
        }

        /// <summary>
        /// Gets the maximum allowed complexity score
        /// </summary>
        public static int GetMaxComplexityScore() => _maxComplexityScore;

        /// <summary>
        /// Removes SQL comments from query for analysis
        /// </summary>
        private static string RemoveComments(string query)
        {
            // Remove block comments
            var result = Regex.Replace(query, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            // Remove line comments
            result = Regex.Replace(result, @"--.*?$", string.Empty, RegexOptions.Multiline);
            return result;
        }

        /// <summary>
        /// Counts the number of JOINs in the query
        /// </summary>
        private static int CountJoins(string upperQuery)
        {
            // Match INNER JOIN, LEFT JOIN, RIGHT JOIN, FULL JOIN, CROSS JOIN
            var joinPattern = @"\b(INNER|LEFT|RIGHT|FULL|CROSS)?\s*JOIN\b";
            return Regex.Matches(upperQuery, joinPattern).Count;
        }

        /// <summary>
        /// Calculates the maximum subquery nesting depth
        /// </summary>
        private static int CalculateSubqueryDepth(string query)
        {
            var upperQuery = query.ToUpperInvariant();
            var maxDepth = 0;
            var currentDepth = 0;
            var inSubquery = false;

            // Look for subquery patterns (SELECT within parentheses)
            var subqueryPatterns = new[]
            {
                @"\(\s*SELECT\b",
                @"\bEXISTS\s*\(\s*SELECT\b",
                @"\bIN\s*\(\s*SELECT\b"
            };

            foreach (var pattern in subqueryPatterns)
            {
                var matches = Regex.Matches(upperQuery, pattern, RegexOptions.IgnoreCase);
                maxDepth = Math.Max(maxDepth, matches.Count > 0 ? 1 : 0);
            }

            // Count nested parentheses with SELECT
            var depth = 0;
            var maxParensDepth = 0;
            for (int i = 0; i < query.Length - 6; i++)
            {
                if (query[i] == '(')
                {
                    depth++;
                    maxParensDepth = Math.Max(maxParensDepth, depth);
                    // Check if followed by SELECT
                    if (i + 7 < query.Length && 
                        query.Substring(i + 1, 6).ToUpperInvariant() == "SELECT")
                    {
                        maxDepth = Math.Max(maxDepth, depth);
                    }
                }
                else if (query[i] == ')')
                {
                    depth = Math.Max(0, depth - 1);
                }
            }

            return maxDepth;
        }

        /// <summary>
        /// Counts UNION and UNION ALL operators
        /// </summary>
        private static int CountUnions(string upperQuery)
        {
            return Regex.Matches(upperQuery, @"\bUNION\b").Count;
        }

        /// <summary>
        /// Counts items in IN clauses (approximate)
        /// </summary>
        private static int CountInClauseItems(string upperQuery)
        {
            // Find IN clauses and count commas within them
            var inClausePattern = @"\bIN\s*\(([^)]*)\)";
            var matches = Regex.Matches(upperQuery, inClausePattern);
            var totalItems = 0;

            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    var content = match.Groups[1].Value;
                    // Count commas + 1 for items
                    var commaCount = content.Count(c => c == ',');
                    totalItems += commaCount + 1;
                }
            }

            return totalItems;
        }

        /// <summary>
        /// Counts SQL function calls
        /// </summary>
        private static int CountFunctionCalls(string upperQuery)
        {
            // Common SQL functions
            var functionPattern = @"\b(COUNT|SUM|AVG|MIN|MAX|LEN|LENGTH|SUBSTRING|CHARINDEX|REPLACE|CAST|CONVERT|ISNULL|COALESCE|CASE)\s*\(";
            return Regex.Matches(upperQuery, functionPattern).Count;
        }

        /// <summary>
        /// Counts wildcards in LIKE patterns
        /// </summary>
        private static int CountWildcards(string upperQuery)
        {
            // Match LIKE patterns and count % and _ wildcards
            var likePattern = "\\bLIKE\\s*['\"]([^'\"]*)['\"]";
            var matches = Regex.Matches(upperQuery, likePattern, RegexOptions.IgnoreCase);
            var totalWildcards = 0;

            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    var pattern = match.Groups[1].Value;
                    totalWildcards += pattern.Count(c => c == '%' || c == '_');
                }
            }

            return totalWildcards;
        }

        /// <summary>
        /// Counts DISTINCT keywords
        /// </summary>
        private static int CountDistinct(string upperQuery)
        {
            return Regex.Matches(upperQuery, @"\bDISTINCT\b").Count;
        }

        /// <summary>
        /// Counts GROUP BY clauses
        /// </summary>
        private static int CountGroupBy(string upperQuery)
        {
            return Regex.Matches(upperQuery, @"\bGROUP\s+BY\b").Count;
        }

        /// <summary>
        /// Counts ORDER BY clauses
        /// </summary>
        private static int CountOrderBy(string upperQuery)
        {
            return Regex.Matches(upperQuery, @"\bORDER\s+BY\b").Count;
        }

        /// <summary>
        /// Counts window functions
        /// </summary>
        private static int CountWindowFunctions(string upperQuery)
        {
            var windowPattern = @"\b(ROW_NUMBER|RANK|DENSE_RANK|NTILE|LAG|LEAD|FIRST_VALUE|LAST_VALUE|SUM|AVG|COUNT|MIN|MAX)\s*\([^)]*\)\s+OVER\s*\(";
            return Regex.Matches(upperQuery, windowPattern, RegexOptions.IgnoreCase).Count;
        }

        /// <summary>
        /// Calculates total complexity score from components
        /// </summary>
        private static int CalculateTotalScore(ComplexityScore score)
        {
            int total = 0;

            // Base score from length (1 point per 100 characters)
            total += score.QueryLength / 100;

            // JOIN penalties (exponential)
            total += score.JoinCount * score.JoinCount * 10;

            // Subquery depth penalty (exponential)
            total += score.SubqueryDepth * score.SubqueryDepth * 50;

            // Union penalty
            total += score.UnionCount * 20;

            // IN clause penalty
            total += score.InClauseCount / 10;

            // Function call penalty
            total += score.FunctionCalls * 5;

            // Wildcard penalty
            total += score.WildcardCount * 2;

            // DISTINCT penalty
            total += score.DistinctCount * 15;

            // GROUP BY penalty
            total += score.GroupByCount * 20;

            // ORDER BY penalty (lower)
            total += score.OrderByCount * 5;

            // Window function penalty (high)
            total += score.WindowFunctionCount * 30;

            return total;
        }

        /// <summary>
        /// Parses an integer from environment variable
        /// </summary>
        private static int ParseIntEnv(string name, int defaultValue)
        {
            var val = Environment.GetEnvironmentVariable(name);
            return int.TryParse(val, out var parsed) && parsed > 0 ? parsed : defaultValue;
        }
    }
}
