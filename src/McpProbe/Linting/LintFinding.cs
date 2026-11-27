using McpProbe.Internal;

namespace McpProbe.Linting;

/// <summary>One lint finding.</summary>
/// <param name="RuleId">The rule id, for example <c>MCPP003</c>.</param>
/// <param name="Severity">The effective severity.</param>
/// <param name="ToolName">The tool the finding is about.</param>
/// <param name="ParameterName">The parameter, for parameter-level rules. Portability findings (MCPP009-MCPP012) use an Inspector-style path such as <c>inputSchema.properties.city</c>.</param>
/// <param name="Message">What is wrong and how to fix it.</param>
public sealed record LintFinding(string RuleId, LintSeverity Severity, string ToolName, string? ParameterName, string Message)
{
    /// <inheritdoc />
    public override string ToString()
    {
        var target = ParameterName is null ? Printable.Text(ToolName, 100) : $"{Printable.Text(ToolName, 100)}.{Printable.Text(ParameterName, 100)}";
        return $"{SeverityLabel(Severity)} {RuleId} {target}: {Printable.Text(Message, 500)}";
    }

    internal static string SeverityLabel(LintSeverity severity) => severity switch
    {
        LintSeverity.Error => "error",
        LintSeverity.Warning => "warning",
        LintSeverity.Info => "info",
        _ => "none",
    };
}
