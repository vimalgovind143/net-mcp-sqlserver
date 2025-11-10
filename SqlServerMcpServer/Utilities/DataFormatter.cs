using System.Text;

namespace SqlServerMcpServer.Utilities
{
    /// <summary>
    /// Provides data formatting utilities for different output formats
    /// </summary>
    public static class DataFormatter
    {
        /// <summary>
        /// Parses a delimiter string into a character
        /// </summary>
        /// <param name="delimiter">Delimiter string (e.g., ",", "tab", "\\t")</param>
        /// <returns>Character delimiter</returns>
        public static char ParseDelimiter(string? delimiter)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(delimiter)) return ',';
                var d = delimiter.Trim();
                if (string.Equals(d, "tab", StringComparison.OrdinalIgnoreCase)) return '\t';
                if (string.Equals(d, "\\t", StringComparison.OrdinalIgnoreCase)) return '\t';
                return d.Length == 1 ? d[0] : ',';
            }
            catch { return ','; }
        }

        /// <summary>
        /// Converts data to CSV format
        /// </summary>
        /// <param name="rows">List of data rows</param>
        /// <param name="columns">List of column names</param>
        /// <param name="delimiter">CSV delimiter character</param>
        /// <returns>CSV string</returns>
        public static string ToCsv(List<Dictionary<string, object>> rows, List<string> columns, char delimiter)
        {
            var sb = new StringBuilder();
            // Header
            for (int i = 0; i < columns.Count; i++)
            {
                if (i > 0) sb.Append(delimiter);
                sb.Append(EscapeCsv(columns[i], delimiter));
            }
            sb.AppendLine();

            foreach (var row in rows)
            {
                for (int i = 0; i < columns.Count; i++)
                {
                    if (i > 0) sb.Append(delimiter);
                    var col = columns[i];
                    row.TryGetValue(col, out var value);
                    var s = value is null ? null : Convert.ToString(value);
                    sb.Append(EscapeCsv(s, delimiter));
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Converts data to HTML table format
        /// </summary>
        /// <param name="rows">List of data rows</param>
        /// <param name="columns">List of column names</param>
        /// <returns>HTML table string</returns>
        public static string ToHtmlTable(List<Dictionary<string, object>> rows, List<string> columns)
        {
            var sb = new StringBuilder();
            sb.Append("<table>");
            sb.Append("<thead><tr>");
            foreach (var col in columns)
            {
                sb.Append("<th>").Append(EscapeHtml(col)).Append("</th>");
            }
            sb.Append("</tr></thead>");
            sb.Append("<tbody>");
            foreach (var row in rows)
            {
                sb.Append("<tr>");
                foreach (var col in columns)
                {
                    row.TryGetValue(col, out var value);
                    var s = value is null ? "NULL" : Convert.ToString(value) ?? string.Empty;
                    sb.Append("<td>").Append(EscapeHtml(s)).Append("</td>");
                }
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table>");
            return sb.ToString();
        }

        /// <summary>
        /// Escapes a value for CSV format
        /// </summary>
        /// <param name="value">Value to escape</param>
        /// <param name="delimiter">CSV delimiter character</param>
        /// <returns>Escaped CSV value</returns>
        private static string EscapeCsv(string? value, char delimiter)
        {
            if (value is null) return "NULL";
            var needsQuote = value.Contains('"') || value.Contains('\n') || value.Contains('\r') || value.Contains(delimiter) || (value.Length > 0 && (value[0] == ' ' || value[^1] == ' '));
            if (!needsQuote) return value;
            var escaped = value.Replace("\"", "\"\"");
            return "\"" + escaped + "\"";
        }

        /// <summary>
        /// Escapes a value for HTML format
        /// </summary>
        /// <param name="value">Value to escape</param>
        /// <returns>Escaped HTML value</returns>
        private static string EscapeHtml(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }
    }
}