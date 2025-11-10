using System.Text.RegularExpressions;

namespace SqlServerMcpServer.Utilities
{
    /// <summary>
    /// Provides query formatting and manipulation utilities
    /// </summary>
    public static class QueryFormatter
    {
        /// <summary>
        /// Applies pagination and limit to a SQL query
        /// </summary>
        /// <param name="query">The original SQL query</param>
        /// <param name="limit">Maximum number of rows to return</param>
        /// <param name="offset">Number of rows to skip</param>
        /// <returns>Modified query with pagination</returns>
        public static string ApplyPaginationAndLimit(string query, int limit, int offset)
        {
            try
            {
                // Remove comments for safer parsing
                var withoutComments = Regex.Replace(query, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
                withoutComments = Regex.Replace(withoutComments, @"--.*?$", string.Empty, RegexOptions.Multiline);

                // If query already has OFFSET/FETCH, modify it
                if (Regex.IsMatch(withoutComments, @"\bOFFSET\s+\d+\s+ROWS\b", RegexOptions.IgnoreCase))
                {
                    // Query already has pagination, just adjust the limit
                    var fetchMatch = Regex.Match(withoutComments, @"\bFETCH\s+NEXT\s+(\d+)\s+ROWS\s+ONLY\b", RegexOptions.IgnoreCase);
                    if (fetchMatch.Success && int.TryParse(fetchMatch.Groups[1].Value, out var existingFetch))
                    {
                        var newFetch = Math.Min(existingFetch, limit);
                        return Regex.Replace(
                            withoutComments,
                            @"\bFETCH\s+NEXT\s+\d+\s+ROWS\s+ONLY\b",
                            $"FETCH NEXT {newFetch} ROWS ONLY",
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                            TimeSpan.FromMilliseconds(100)
                        );
                    }
                    return query;
                }

                // If query has TOP, modify it
                var topMatch = Regex.Match(withoutComments, @"\bSELECT\s+(DISTINCT\s+)?TOP\s+(\d+)\b", RegexOptions.IgnoreCase);
                if (topMatch.Success)
                {
                    if (int.TryParse(topMatch.Groups[2].Value, out var existing))
                    {
                        var newTop = Math.Min(existing, limit);
                        if (newTop != existing)
                        {
                            var capped = Regex.Replace(
                                withoutComments,
                                @"\bSELECT\s+(DISTINCT\s+)?TOP\s+\d+\b",
                                m =>
                                {
                                    var distinct = m.Groups[1].Value;
                                    return $"SELECT {distinct}TOP {newTop}";
                                },
                                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                                TimeSpan.FromMilliseconds(100)
                            );
                            return string.IsNullOrWhiteSpace(capped) ? query : capped;
                        }
                    }
                    return query;
                }

                // Add OFFSET/FETCH for pagination if offset > 0
                if (offset > 0)
                {
                    var withOffset = Regex.Replace(
                        withoutComments,
                        @"\bSELECT\s+(DISTINCT\s+)?",
                        m =>
                        {
                            var distinct = m.Groups[1].Value;
                            return $"SELECT {distinct}";
                        },
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                        TimeSpan.FromMilliseconds(100)
                    );
                    
                    return $"{withOffset} OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY";
                }

                // No pagination or TOP: insert TOP limit after first SELECT
                var replaced = Regex.Replace(
                    withoutComments,
                    @"\bSELECT\s+(DISTINCT\s+)?",
                    m =>
                    {
                        var distinct = m.Groups[1].Value;
                        return $"SELECT {distinct}TOP {limit} ";
                    },
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100)
                );

                return string.IsNullOrWhiteSpace(replaced) ? query : replaced;
            }
            catch
            {
                return query;
            }
        }

        /// <summary>
        /// Applies a TOP limit to a SQL query
        /// </summary>
        /// <param name="query">The original SQL query</param>
        /// <param name="maxRows">Maximum number of rows to return</param>
        /// <returns>Modified query with TOP limit</returns>
        public static string ApplyTopLimit(string query, int maxRows)
        {
            try
            {
                // Remove comments for safer parsing
                var withoutComments = Regex.Replace(query, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
                withoutComments = Regex.Replace(withoutComments, @"--.*?$", string.Empty, RegexOptions.Multiline);

                // If query already has OFFSET/FETCH, modify it
                if (Regex.IsMatch(withoutComments, @"\bOFFSET\s+\d+\s+ROWS\b", RegexOptions.IgnoreCase))
                {
                    // Query already has pagination, just adjust the limit
                    var fetchMatch = Regex.Match(withoutComments, @"\bFETCH\s+NEXT\s+(\d+)\s+ROWS\s+ONLY\b", RegexOptions.IgnoreCase);
                    if (fetchMatch.Success && int.TryParse(fetchMatch.Groups[1].Value, out var existingFetch))
                    {
                        var newFetch = Math.Min(existingFetch, maxRows);
                        return Regex.Replace(
                            withoutComments,
                            @"\bFETCH\s+NEXT\s+\d+\s+ROWS\s+ONLY\b",
                            $"FETCH NEXT {newFetch} ROWS ONLY",
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                            TimeSpan.FromMilliseconds(100)
                        );
                    }
                    return query;
                }

                // If query has TOP, modify it
                var topMatch = Regex.Match(withoutComments, @"\bSELECT\s+(DISTINCT\s+)?TOP\s+(\d+)\b", RegexOptions.IgnoreCase);
                if (topMatch.Success)
                {
                    if (int.TryParse(topMatch.Groups[2].Value, out var existing))
                    {
                        var newTop = Math.Min(existing, maxRows);
                        if (newTop != existing)
                        {
                            var capped = Regex.Replace(
                                withoutComments,
                                @"\bSELECT\s+(DISTINCT\s+)?TOP\s+\d+\b",
                                m =>
                                {
                                    var distinct = m.Groups[1].Value;
                                    return $"SELECT {distinct}TOP {newTop}";
                                },
                                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                                TimeSpan.FromMilliseconds(100)
                            );
                            return string.IsNullOrWhiteSpace(capped) ? query : capped;
                        }
                    }
                    return query;
                }

                // Note: ApplyTopLimit doesn't support offset, only maxRows limit
                // For offset support, use ApplyPaginationAndLimit method instead

                // No pagination or TOP: insert TOP limit after first SELECT
                var replaced = Regex.Replace(
                    withoutComments,
                    @"\bSELECT\s+(DISTINCT\s+)?",
                    m =>
                    {
                        var distinct = m.Groups[1].Value;
                        return $"SELECT {distinct}TOP {maxRows} ";
                    },
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100)
                );

                return string.IsNullOrWhiteSpace(replaced) ? query : replaced;
            }
            catch
            {
                return query;
            }
        }
    }
}