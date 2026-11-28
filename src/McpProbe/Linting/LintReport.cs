using System.Globalization;
using System.Text;
using McpProbe.Internal;

namespace McpProbe.Linting;

/// <summary>The findings of a lint run.</summary>
public sealed class LintReport
{
    internal LintReport(IReadOnlyList<LintFinding> findings, LintSeverity failOn)
    {
        Findings = findings;
        FailOn = failOn;
    }

    /// <summary>Gets the findings, most severe first, then by tool, parameter and rule.</summary>
    public IReadOnlyList<LintFinding> Findings { get; }

    /// <summary>Gets the lowest severity that counts as a failure.</summary>
    public LintSeverity FailOn { get; }

    /// <summary>
    /// Gets whether the run failed. Findings from the existing rules fail when they are at or above <see cref="FailOn"/>.
    /// Findings from the schema-portability rules (MCPP009-MCPP012) fail only when their effective severity is
    /// <see cref="LintSeverity.Error"/>; <see cref="FailOn"/> does not change that floor.
    /// </summary>
    public bool Failed => Findings.Any(Fails);

    private bool Fails(LintFinding finding)
    {
        if (LintRules.IsPortabilityRule(finding.RuleId))
        {
            return finding.Severity == LintSeverity.Error;
        }

        return FailOn != LintSeverity.None && finding.Severity >= FailOn;
    }

    /// <summary>Formats the findings as plain text, one per line.</summary>
    /// <returns>The report.</returns>
    public string ToReport()
    {
        if (Findings.Count == 0)
        {
            return "No lint findings.";
        }

        var sb = new StringBuilder();
        sb.Append(Summary());
        foreach (var finding in Findings)
        {
            sb.Append('\n').Append("  ").Append(finding);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats the findings as a complete, self-contained HTML document: inline CSS, no scripts and no external assets.
    /// Server-supplied text is already sanitized and HTML-encoded, so the result is safe to write to a file.
    /// </summary>
    /// <returns>The HTML document.</returns>
    public string ToHtml()
    {
        var rows = Findings.Select(f => (RowClass: LintFinding.SeverityLabel(f.Severity), Cells: new[]
        {
            LintFinding.SeverityLabel(f.Severity),
            f.RuleId,
            Printable.Text(f.ToolName, 100),
            f.ParameterName is null ? string.Empty : Printable.Text(f.ParameterName, 100),
            Printable.Text(f.Message, 500),
            Fails(f) ? "yes" : "no",
        }));
        return HtmlReport.Page(
            "McpProbe lint report",
            Failed ? "fail" : "pass",
            Failed ? "Failed" : "Passed",
            Findings.Count == 0 ? "No lint findings." : $"{Summary()} Fails on: {LintFinding.SeverityLabel(FailOn)}.",
            ["Severity", "Rule", "Tool", "Path", "Message", "Fails the run"],
            rows);
    }

    /// <summary>Throws when <see cref="Failed"/> is <see langword="true"/>.</summary>
    /// <exception cref="McpProbeAssertionException">A finding failed the run (see <see cref="Failed"/>).</exception>
    public void ThrowIfFailed()
    {
        if (Failed)
        {
            throw new McpProbeAssertionException(
                $"Lint failed.\n{ToReport()}");
        }
    }

    /// <inheritdoc />
    public override string ToString() => ToReport();

    private string Summary() => string.Create(CultureInfo.InvariantCulture,
        $"{Findings.Count} lint finding(s): {Count(LintSeverity.Error)} error, {Count(LintSeverity.Warning)} warning, {Count(LintSeverity.Info)} info.");

    private int Count(LintSeverity severity) => Findings.Count(f => f.Severity == severity);
}
