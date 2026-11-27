namespace McpProbe.Snapshots;

/// <summary>Options for tool snapshot checks.</summary>
public sealed class SnapshotOptions
{
    /// <summary>The environment variable that turns on update mode (<c>1</c> or <c>true</c>).</summary>
    public const string UpdateEnvironmentVariable = "MCPPROBE_UPDATE_SNAPSHOTS";

    /// <summary>
    /// Gets or sets whether to overwrite the snapshot with the current surface instead of checking it.
    /// When <see langword="null"/> (the default), update mode is on if <c>MCPPROBE_UPDATE_SNAPSHOTS</c> is <c>1</c> or <c>true</c>.
    /// </summary>
    public bool? Update { get; set; }

    /// <summary>
    /// Gets or sets whether non-breaking changes also fail the check. Defaults to <see langword="false"/>:
    /// non-breaking changes are reported through <see cref="Log"/> and returned in the diff.
    /// </summary>
    public bool FailOnNonBreakingChanges { get; set; }

    /// <summary>
    /// Gets or sets whether a missing snapshot is written (and the check fails once, so you review and commit it).
    /// When <see langword="null"/> (the default), missing snapshots are written locally but not on CI
    /// (when <c>CI</c> or <c>TF_BUILD</c> is set), where a missing snapshot is always an error.
    /// </summary>
    public bool? CreateMissing { get; set; }

    /// <summary>
    /// Gets or sets the directory a relative snapshot path is resolved against.
    /// When <see langword="null"/>, the directory of the calling source file is used.
    /// </summary>
    public string? Directory { get; set; }

    /// <summary>Gets or sets a callback that receives the report when the check passes with non-breaking changes.</summary>
    public Action<string>? Log { get; set; }

    internal bool ResolveUpdate() => Update ?? IsTruthy(Environment.GetEnvironmentVariable(UpdateEnvironmentVariable));

    internal bool ResolveCreateMissing() => CreateMissing ?? !IsCi();

    internal static bool IsCi() =>
        IsTruthy(Environment.GetEnvironmentVariable("CI")) || IsTruthy(Environment.GetEnvironmentVariable("TF_BUILD"));

    private static bool IsTruthy(string? value) =>
        value is not null && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
}
