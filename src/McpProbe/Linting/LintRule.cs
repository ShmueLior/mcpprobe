namespace McpProbe.Linting;

/// <summary>Describes a lint rule.</summary>
/// <param name="Id">The stable rule id, for example <c>MCPP001</c>.</param>
/// <param name="Title">A short title.</param>
/// <param name="DefaultSeverity">The severity used when the options don't override it.</param>
public sealed record LintRule(string Id, string Title, LintSeverity DefaultSeverity);

/// <summary>The built-in lint rules.</summary>
public static class LintRules
{
    /// <summary>MCPP001: a tool has no description.</summary>
    public static readonly LintRule ToolMissingDescription = new("MCPP001", "Tool has no description", LintSeverity.Error);

    /// <summary>MCPP002: a tool description is shorter than <see cref="LintOptions.MinToolDescriptionLength"/>.</summary>
    public static readonly LintRule ToolDescriptionTooShort = new("MCPP002", "Tool description is too short", LintSeverity.Warning);

    /// <summary>MCPP003: a parameter has no description.</summary>
    public static readonly LintRule ParameterMissingDescription = new("MCPP003", "Parameter has no description", LintSeverity.Warning);

    /// <summary>MCPP004: a tool name doesn't follow the MCP naming guidance (1-128 characters: letters, digits, '_', '-', '.').</summary>
    public static readonly LintRule InvalidToolName = new("MCPP004", "Tool name doesn't follow MCP naming guidance", LintSeverity.Warning);

    /// <summary>MCPP005: tool names mix naming styles (snake_case, camelCase, kebab-case, PascalCase).</summary>
    public static readonly LintRule InconsistentToolNaming = new("MCPP005", "Tool names mix naming styles", LintSeverity.Warning);

    /// <summary>MCPP006: parameter names mix naming styles across the server.</summary>
    public static readonly LintRule InconsistentParameterNaming = new("MCPP006", "Parameter names mix naming styles", LintSeverity.Warning);

    /// <summary>MCPP007: a parameter has a vague name such as <c>data</c> or <c>value</c>.</summary>
    public static readonly LintRule VagueParameterName = new("MCPP007", "Parameter name is vague", LintSeverity.Info);

    /// <summary>Gets all built-in rules, ordered by id.</summary>
    public static IReadOnlyList<LintRule> All { get; } =
    [
        ToolMissingDescription,
        ToolDescriptionTooShort,
        ParameterMissingDescription,
        InvalidToolName,
        InconsistentToolNaming,
        InconsistentParameterNaming,
        VagueParameterName,
    ];
}
