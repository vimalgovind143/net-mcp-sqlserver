namespace SqlServerMcpServer.Configuration
{
    /// <summary>
    /// Represents a named SQL Server connection entry in the connection registry.
    /// </summary>
    public class ConnectionInfo
    {
        /// <summary>Unique name for this connection (e.g. "default", "prod", "reporting").</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Full ADO.NET connection string.</summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>Parsed server/host name from the connection string.</summary>
        public string ServerName { get; set; } = string.Empty;

        /// <summary>Current logical database name on this connection.</summary>
        public string CurrentDatabase { get; set; } = string.Empty;

        /// <summary>When this connection entry was registered.</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>Last time a SqlConnection was created from this entry.</summary>
        public DateTime? LastUsed { get; set; }

        /// <summary>Whether this is the currently active (default) connection.</summary>
        public bool IsActive { get; set; }
    }
}
