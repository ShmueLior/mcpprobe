using System.Globalization;
using System.Text.Json.Nodes;
using McpProbe.Internal;
using McpProbe.Snapshots;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpProbe.Linting;

/// <summary>
/// Checks tool names, descriptions and parameters for problems that make tools harder for models to use.
/// </summary>
/// <remarks>
/// Parameter rules check top-level parameters. With <see cref="LintOptions.CheckNestedParameters"/> they also check nested
/// object properties and array items, reported as <c>address.zip</c> or <c>stops[].city</c>, following local <c>$ref</c>s.
/// With <see cref="LintOptions.Strict"/>, MCPP009-MCPP012 also walk <c>inputSchema</c> and <c>outputSchema</c> for
/// portability issues (paths such as <c>inputSchema.properties.city</c>).
/// </remarks>
public static class ToolLinter
{
    /// <summary>Lints protocol tools.</summary>
    /// <param name="tools">The tools.</param>
    /// <param name="options">Rule options. Defaults to <see cref="LintOptions"/> defaults.</param>
    /// <returns>The findings.</returns>
    public static LintReport Lint(IEnumerable<Tool> tools, LintOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        return Lint(ToolSurface.FromTools(tools), options);
    }

    /// <summary>Lints client tools.</summary>
    /// <param name="tools">The tools.</param>
    /// <param name="options">Rule options. Defaults to <see cref="LintOptions"/> defaults.</param>
    /// <returns>The findings.</returns>
    public static LintReport Lint(IEnumerable<McpClientTool> tools, LintOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        return Lint(ToolSurface.FromClientTools(tools), options);
    }

    /// <summary>Lints a captured tool surface.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="options">Rule options. Defaults to <see cref="LintOptions"/> defaults.</param>
    /// <returns>The findings.</returns>
    public static LintReport Lint(ToolSurface surface, LintOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        options ??= new LintOptions();
        if (options.MinToolDescriptionLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MinToolDescriptionLength can't be negative.");
        }

        var findings = new List<LintFinding>();
        var parameterNames = new List<(string Tool, string Path, string Name)>();

        foreach (var tool in surface.Tools)
        {
            CheckToolName(tool.Name, options, findings);
            CheckToolDescription(tool, options, findings);

            var schema = JsonSchemaReader.FromElement(tool.InputSchema);
            LintProperties(new Walk(tool.Name, schema, options, findings, parameterNames), schema, prefix: null);
            SchemaPortabilityLinter.Lint(tool, options, findings);
        }

        CheckConsistency(
            surface.Tools.Select(t => (t.Name, (string?)null, t.Name)),
            LintRules.InconsistentToolNaming,
            "Tool",
            options,
            findings);
        CheckConsistency(
            parameterNames.Select(p => (p.Tool, (string?)p.Path, p.Name)),
            LintRules.InconsistentParameterNaming,
            "Parameter",
            options,
            findings);

        var ordered = findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.ToolName, StringComparer.Ordinal)
            .ThenBy(f => f.ParameterName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(f => f.RuleId, StringComparer.Ordinal)
            .ToList();
        return new LintReport(ordered, options.FailOn);
    }

    private static void LintProperties(Walk walk, JsonNode? schema, string? prefix)
    {
        var nested = walk.Options.CheckNestedParameters;

        // Follow local refs. Each schema is linted once, at the first path that reaches it, so a type used
        // twice or a recursive type doesn't repeat its findings.
        if (nested && JsonSchemaReader.GetString(schema, "$ref") is { } reference && JsonSchemaReader.TryFindTarget(reference, walk.Root, out var target))
        {
            schema = target;
        }

        if (schema is null || !walk.Linted.Add(schema))
        {
            return;
        }

        var (tool, options, findings) = (walk.Tool, walk.Options, walk.Findings);
        if (nested && JsonSchemaReader.Get(schema, "items") is JsonObject items)
        {
            LintProperties(walk, items, prefix + "[]");
        }

        var properties = JsonSchemaReader.GetProperties(schema);
        if (properties is null)
        {
            return;
        }

        foreach (var (name, property) in properties)
        {
            var path = prefix is null ? name : $"{prefix}.{name}";
            var noun = prefix is null ? "Parameter" : "Property";
            walk.ParameterNames.Add((tool, path, name));

            // A description next to a $ref counts, and so does the referenced schema's own.
            var described = nested && JsonSchemaReader.TryResolveRef(property, walk.Root, out var resolved, out _) ? resolved : property;
            if (string.IsNullOrWhiteSpace(JsonSchemaReader.GetString(described, "description")))
            {
                Add(findings, options, LintRules.ParameterMissingDescription, tool, path,
                    $"{noun} '{path}' has no description. Say what it is and what format it expects.");
            }

            if (options.VagueParameterNames.Contains(name))
            {
                Add(findings, options, LintRules.VagueParameterName, tool, path,
                    $"{noun} name '{path}' is vague. Use a name that says what the value is (for example 'city' instead of 'value').");
            }

            if (nested)
            {
                LintProperties(walk, property, path);
            }
        }
    }

    private static void CheckToolName(string name, LintOptions options, List<LintFinding> findings)
    {
        if (name.Length is < 1 or > 128 || !name.All(IsAllowedToolNameChar))
        {
            Add(findings, options, LintRules.InvalidToolName, name, null,
                "Tool names should be 1-128 characters and use only ASCII letters, digits, '_', '-' and '.'.");
        }
    }

    private static bool IsAllowedToolNameChar(char c) =>
        c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-' or '.';

    private static void CheckToolDescription(ToolContract tool, LintOptions options, List<LintFinding> findings)
    {
        var description = tool.Description?.Trim();
        if (string.IsNullOrEmpty(description))
        {
            Add(findings, options, LintRules.ToolMissingDescription, tool.Name, null,
                "Tool has no description. Models pick tools by their description; say what it does and when to use it.");
        }
        else if (description.Length < options.MinToolDescriptionLength)
        {
            Add(findings, options, LintRules.ToolDescriptionTooShort, tool.Name, null,
                string.Create(CultureInfo.InvariantCulture,
                    $"Description is {description.Length} characters (minimum {options.MinToolDescriptionLength}). Say what the tool does and when to use it."));
        }
    }

    private static void CheckConsistency(
        IEnumerable<(string Tool, string? Parameter, string Name)> items,
        LintRule rule,
        string noun,
        LintOptions options,
        List<LintFinding> findings)
    {
        if (options.SeverityOf(rule) == LintSeverity.None)
        {
            return;
        }

        var classified = items
            .Select(i => (i.Tool, i.Parameter, i.Name, Style: NamingStyles.Detect(i.Name)))
            .Where(i => i.Style != NamingStyle.Lower)
            .ToList();

        // Single lowercase words fit any style, so they never decide the majority or get flagged.
        var counts = classified
            .Where(i => i.Style != NamingStyle.Other)
            .GroupBy(i => i.Style)
            .Select(g => (Style: g.Key, Count: g.Count()))
            .ToList();
        if (counts.Count == 0)
        {
            return;
        }

        // Majority wins; ties go to the style used first (inputs are sorted by tool name).
        var majority = counts
            .OrderByDescending(c => c.Count)
            .ThenBy(c => classified.FindIndex(i => i.Style == c.Style))
            .First()
            .Style;

        foreach (var item in classified.Where(i => i.Style != majority))
        {
            Add(findings, options, rule, item.Tool, item.Parameter,
                $"{noun} name '{item.Name}' is {NamingStyles.Label(item.Style)}, but most {noun.ToLowerInvariant()} names on this server are {NamingStyles.Label(majority)}.");
        }
    }

    private static void Add(List<LintFinding> findings, LintOptions options, LintRule rule, string tool, string? parameter, string message)
    {
        var severity = options.SeverityOf(rule);
        if (severity != LintSeverity.None)
        {
            findings.Add(new LintFinding(rule.Id, severity, tool, parameter, message));
        }
    }

    private sealed class Walk(string tool, JsonNode? root, LintOptions options, List<LintFinding> findings, List<(string Tool, string Path, string Name)> parameterNames)
    {
        public string Tool { get; } = tool;

        public JsonNode? Root { get; } = root;

        public LintOptions Options { get; } = options;

        public List<LintFinding> Findings { get; } = findings;

        public List<(string Tool, string Path, string Name)> ParameterNames { get; } = parameterNames;

        public HashSet<JsonNode> Linted { get; } = new(ReferenceEqualityComparer.Instance);
    }
}
