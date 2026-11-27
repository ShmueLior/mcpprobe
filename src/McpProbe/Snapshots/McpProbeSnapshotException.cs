namespace McpProbe.Snapshots;

/// <summary>
/// Thrown when a tool snapshot check fails: the snapshot is missing or unreadable, or the surface changed in a way the options don't allow.
/// </summary>
public sealed class McpProbeSnapshotException : McpProbeAssertionException
{
    /// <summary>Initializes a new instance with no message.</summary>
    public McpProbeSnapshotException()
    {
    }

    /// <summary>Initializes a new instance with a message.</summary>
    /// <param name="message">The failure description.</param>
    public McpProbeSnapshotException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and an inner exception.</summary>
    /// <param name="message">The failure description.</param>
    /// <param name="innerException">The underlying error.</param>
    public McpProbeSnapshotException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal McpProbeSnapshotException(string message, string snapshotPath, ToolSurfaceDiff? diff)
        : base(message)
    {
        SnapshotPath = snapshotPath;
        Diff = diff;
    }

    /// <summary>Gets the snapshot file path, when known.</summary>
    public string? SnapshotPath { get; }

    /// <summary>Gets the diff that caused the failure, when the snapshot existed.</summary>
    public ToolSurfaceDiff? Diff { get; }
}
