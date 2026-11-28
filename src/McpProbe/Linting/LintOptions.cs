namespace McpProbe.Linting;

/// <summary>Options for <see cref="ToolLinter"/>.</summary>
public sealed class LintOptions
{
    /// <summary>Gets or sets the minimum tool description length, in characters, for MCPP002. Defaults to 20.</summary>
    public int MinToolDescriptionLength { get; set; } = 20;

    /// <summary>
    /// Gets or sets how many words a description needs to be exempt from MCPP002 when it is shorter than
    /// <see cref="MinToolDescriptionLength"/>. Defaults to 4, so "Delete a file share" passes. Set 0 to check length only, as before.
    /// </summary>
    public int MinToolDescriptionWords { get; set; } = 4;

    /// <summary>Gets the parameter names MCPP007 treats as vague. Compared case-insensitively.</summary>
    public ISet<string> VagueParameterNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "arg", "args", "data", "input", "item", "obj", "object", "param", "params", "payload", "str", "thing", "val", "value", "x",
    };

    /// <summary>
    /// Gets the tool name segments (such as <c>create</c> in <c>keyvault_secret_create</c>) that make MCPP007 skip a top-level
    /// <c>value</c>, <c>data</c>, <c>payload</c> or similar parameter, because the tool name says what it is. Names are split
    /// on '_', '-', '.' and case changes. Compared case-insensitively. Clear it to check those parameters everywhere.
    /// </summary>
    public ISet<string> PayloadContextTokens { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "add", "create", "insert", "patch", "post", "publish", "put", "replace", "send", "set", "update", "upsert", "write",
    };

    /// <summary>
    /// Gets the name segments MCPP013 treats as destructive, such as <c>delete</c> in <c>delete_file</c> or <c>DeleteFile</c>.
    /// Names are split on '_', '-', '.' and case changes; a whole segment must match. Compared case-insensitively.
    /// </summary>
    public ISet<string> DestructiveNameTokens { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "clear", "delete", "destroy", "drop", "erase", "purge", "remove", "truncate", "unlink", "wipe",
    };

    /// <summary>
    /// Gets the description phrases MCPP014 reads as a claim of structured results, such as <c>returns JSON</c>.
    /// Matched case-insensitively as whole words, with runs of whitespace treated as one space.
    /// </summary>
    public ISet<string> StructuredOutputCues { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "output schema", "returns a json", "returns json", "structured content", "structured output", "structured result",
    };

    /// <summary>
    /// Gets per-rule severity overrides, keyed by rule id (for example <c>MCPP007</c>).
    /// Use <see cref="LintSeverity.None"/> to turn a rule off.
    /// </summary>
    public IDictionary<string, LintSeverity> Severities { get; } = new Dictionary<string, LintSeverity>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the lowest severity that fails <see cref="LintReport.ThrowIfFailed"/> and the assertion helpers
    /// for MCPP001-MCPP007 and MCPP013-MCPP016. Defaults to <see cref="LintSeverity.Warning"/>.
    /// Set it to <see cref="LintSeverity.None"/> so those rules never fail. Findings from the schema-portability
    /// rules (MCPP009-MCPP012) fail only when their effective severity is <see cref="LintSeverity.Error"/>;
    /// this property does not change that floor.
    /// </summary>
    public LintSeverity FailOn { get; set; } = LintSeverity.Warning;

    /// <summary>
    /// Gets or sets whether MCPP003, MCPP006 and MCPP007 also check nested object properties and array items
    /// (reported as <c>address.zip</c> or <c>stops[].city</c>), following local <c>$ref</c>s. Defaults to <see langword="false"/>:
    /// only top-level parameters are checked.
    /// </summary>
    public bool CheckNestedParameters { get; set; }

    /// <summary>
    /// Gets or sets whether to run the schema-portability rules MCPP009-MCPP012 (bare boolean schemas, type unions,
    /// untyped schemas and remote <c>$ref</c>s), matching MCP Inspector's <c>--strict</c> checks. Defaults to <see langword="false"/>.
    /// A portability rule also runs when its id is set in <see cref="Severities"/>, even if this is <see langword="false"/>;
    /// <see cref="LintSeverity.None"/> still turns one off. Findings from those rules fail the report only when their
    /// effective severity is <see cref="LintSeverity.Error"/>; <see cref="FailOn"/> does not change that floor.
    /// </summary>
    public bool Strict { get; set; }

    internal LintSeverity SeverityOf(LintRule rule) =>
        Severities.TryGetValue(rule.Id, out var severity) ? severity : rule.DefaultSeverity;

    internal bool IsPortabilityRuleEnabled(LintRule rule) =>
        SeverityOf(rule) != LintSeverity.None && (Strict || Severities.ContainsKey(rule.Id));
}
