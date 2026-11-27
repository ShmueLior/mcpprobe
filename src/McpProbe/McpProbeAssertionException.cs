namespace McpProbe;

/// <summary>
/// Thrown when an McpProbe assertion fails. It is a plain exception, so every test framework reports it as a failure.
/// </summary>
public class McpProbeAssertionException : Exception
{
    /// <summary>Initializes a new instance with no message.</summary>
    public McpProbeAssertionException()
    {
    }

    /// <summary>Initializes a new instance with a message.</summary>
    /// <param name="message">What was expected and what was found.</param>
    public McpProbeAssertionException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and an inner exception.</summary>
    /// <param name="message">What was expected and what was found.</param>
    /// <param name="innerException">The underlying error.</param>
    public McpProbeAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
