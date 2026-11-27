namespace McpProbe.Linting;

/// <summary>Options for <see cref="ToolLinter"/>.</summary>
public sealed class LintOptions
{
    /// <summary>Gets or sets the minimum tool description length, in characters, for MCPP002. Defaults to 20.</summary>
    public int MinToolDescriptionLength { get; set; } = 20;

    /// <summary>Gets the parameter names MCPP007 treats as vague. Compared case-insensitively.</summary>
    public ISet<string> VagueParameterNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "arg", "args", "data", "input", "item", "obj", "object", "param", "params", "payload", "str", "thing", "val", "value", "x",
    };

    /// <summary>
    /// Gets per-rule severity overrides, keyed by rule id (for example <c>MCPP007</c>).
    /// Use <see cref="LintSeverity.None"/> to turn a rule off.
    /// </summary>
    public IDictionary<string, LintSeverity> Severities { get; } = new Dictionary<string, LintSeverity>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the lowest severity that fails <see cref="LintReport.ThrowIfFailed"/> and the assertion helpers. Defaults to <see cref="LintSeverity.Warning"/>.
    /// Set it to <see cref="LintSeverity.None"/> to never fail.
    /// </summary>
    public LintSeverity FailOn { get; set; } = LintSeverity.Warning;

    /// <summary>
    /// Gets or sets whether MCPP003, MCPP006 and MCPP007 also check nested object properties and array items
    /// (reported as <c>address.zip</c> or <c>stops[].city</c>), following local <c>$ref</c>s. Defaults to <see langword="false"/>:
    /// only top-level parameters are checked.
    /// </summary>
    public bool CheckNestedParameters { get; set; }

    internal LintSeverity SeverityOf(LintRule rule) =>
        Severities.TryGetValue(rule.Id, out var severity) ? severity : rule.DefaultSeverity;
}
