using System.Collections.Generic;

namespace SqlServerMcpServer.Utilities
{
    /// <summary>
    /// Encapsulates context information for an error
    /// </summary>
    public class ErrorContext
    {
        /// <summary>
        /// Gets or sets the error code
        /// </summary>
        public ErrorCode Code { get; set; }

        /// <summary>
        /// Gets or sets the primary error message
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Gets or sets the SQL Server error number (if applicable)
        /// </summary>
        public int? SqlErrorNumber { get; set; }

        /// <summary>
        /// Gets or sets the SQL error line number (if applicable)
        /// </summary>
        public int? SqlErrorLineNumber { get; set; }

        /// <summary>
        /// Gets or sets the operation that was being performed
        /// </summary>
        public string Operation { get; set; }

        /// <summary>
        /// Gets or sets the query that caused the error (if applicable)
        /// </summary>
        public string Query { get; set; }

        /// <summary>
        /// Gets or sets additional error details
        /// </summary>
        public Dictionary<string, object> Details { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Gets or sets troubleshooting steps
        /// </summary>
        public List<string> TroubleshootingSteps { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets suggested fixes
        /// </summary>
        public List<string> SuggestedFixes { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets documentation links
        /// </summary>
        public List<string> DocumentationLinks { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets whether this is a transient error that can be retried
        /// </summary>
        public bool IsTransient { get; set; }

        /// <summary>
        /// Gets or sets the inner exception message
        /// </summary>
        public string InnerException { get; set; }

        /// <summary>
        /// Gets or sets the stack trace (for debugging)
        /// </summary>
        public string StackTrace { get; set; }

        /// <summary>
        /// Gets or sets context-specific information
        /// </summary>
        public Dictionary<string, string> Context { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Creates a new ErrorContext with basic information
        /// </summary>
        /// <param name="code">Error code</param>
        /// <param name="message">Error message</param>
        /// <param name="operation">Operation name</param>
        public ErrorContext(ErrorCode code, string message, string operation = null)
        {
            Code = code;
            Message = message;
            Operation = operation;
        }

        /// <summary>
        /// Determines if this error is likely transient and can be retried
        /// </summary>
        /// <returns>True if the error is transient</returns>
        public bool CanRetry()
        {
            // Check explicit transient flag
            if (IsTransient) return true;

            // Check error code for known transient errors
            return Code switch
            {
                ErrorCode.ConnectionTimeout => true,
                ErrorCode.QueryTimeout => true,
                ErrorCode.DatabaseUnavailable => true,
                ErrorCode.ConnectionFailed => true,
                _ => false
            };
        }

        /// <summary>
        /// Gets a user-friendly summary of the error
        /// </summary>
        /// <returns>Formatted error summary</returns>
        public string GetSummary()
        {
            var summary = $"{Code.GetDescription()}: {Message}";
            if (!string.IsNullOrEmpty(Operation))
            {
                summary += $" (Operation: {Operation})";
            }
            return summary;
        }
    }
}
