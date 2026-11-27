namespace McpProbe.Cli;

/// <summary>Process exit codes. CI scripts can rely on these.</summary>
internal static class ExitCodes
{
    /// <summary>The check passed.</summary>
    public const int Success = 0;

    /// <summary>The check failed: a breaking change, or lint findings at or above the threshold.</summary>
    public const int CheckFailed = 1;

    /// <summary>Bad arguments, a missing or unreadable file, or the server couldn't be reached.</summary>
    public const int Error = 2;
}
