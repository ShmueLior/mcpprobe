using System.Globalization;
using McpProbe.Internal;
using McpProbe.Linting;
using ModelContextProtocol.Protocol;

namespace McpProbe.Assertions;

/// <summary>Assertions for a list of tools.</summary>
public sealed class ToolListAssertions
{
    internal ToolListAssertions(IReadOnlyList<Tool> tools) => Tools = tools;

    /// <summary>Gets the tools under test.</summary>
    public IReadOnlyList<Tool> Tools { get; }

    /// <summary>Asserts a tool with this exact name exists.</summary>
    /// <param name="name">The tool name (case-sensitive).</param>
    /// <returns>Assertions for that tool.</returns>
    public ToolAssertions ContainsTool(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var tool = Tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
        if (tool is null)
        {
            var hint = Tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) is { } near
                ? $" Did you mean '{near.Name}'? Tool names are case-sensitive."
                : string.Empty;
            throw Fail.With($"Expected a tool named '{name}', but there is none.{hint} Tools: {Fail.List(Tools.Select(t => t.Name))}.");
        }

        return new ToolAssertions(tool);
    }

    /// <summary>Asserts no tool has this name.</summary>
    /// <param name="name">The tool name (case-sensitive).</param>
    /// <returns>The same assertions, for chaining.</returns>
    public ToolListAssertions DoesNotContainTool(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Tools.Any(t => string.Equals(t.Name, name, StringComparison.Ordinal)))
        {
            throw Fail.With($"Expected no tool named '{name}', but it exists.");
        }

        return this;
    }

    /// <summary>Asserts the exact number of tools.</summary>
    /// <param name="expected">The expected count.</param>
    /// <returns>The same assertions, for chaining.</returns>
    public ToolListAssertions HasCount(int expected)
    {
        if (Tools.Count != expected)
        {
            throw Fail.With(string.Create(CultureInfo.InvariantCulture,
                $"Expected {expected} tool(s), but found {Tools.Count}: {Fail.List(Tools.Select(t => t.Name))}."));
        }

        return this;
    }

    /// <summary>Lints the tools and fails when <see cref="LintReport.Failed"/> is <see langword="true"/>.</summary>
    /// <param name="options">Lint options.</param>
    /// <returns>The lint report (it may contain findings below the fail threshold).</returns>
    public LintReport PassesLint(LintOptions? options = null)
    {
        var report = ToolLinter.Lint(Tools, options);
        report.ThrowIfFailed();
        return report;
    }
}
