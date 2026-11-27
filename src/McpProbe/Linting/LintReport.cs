using System.Globalization;
using System.Text;

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

    /// <summary>Gets whether any finding is at or above <see cref="FailOn"/>.</summary>
    public bool Failed => FailOn != LintSeverity.None && Findings.Any(f => f.Severity >= FailOn);

    /// <summary>Formats the findings as plain text, one per line.</summary>
    /// <returns>The report.</returns>
    public string ToReport()
    {
        if (Findings.Count == 0)
        {
            return "No lint findings.";
        }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"{Findings.Count} lint finding(s): {Count(LintSeverity.Error)} error, {Count(LintSeverity.Warning)} warning, {Count(LintSeverity.Info)} info.");
        foreach (var finding in Findings)
        {
            sb.Append('\n').Append("  ").Append(finding);
        }

        return sb.ToString();
    }

    /// <summary>Throws when <see cref="Failed"/> is <see langword="true"/>.</summary>
    /// <exception cref="McpProbeAssertionException">A finding is at or above <see cref="FailOn"/>.</exception>
    public void ThrowIfFailed()
    {
        if (Failed)
        {
            throw new McpProbeAssertionException(
                $"Lint failed (fail-on: {LintFinding.SeverityLabel(FailOn)}).\n{ToReport()}");
        }
    }

    /// <inheritdoc />
    public override string ToString() => ToReport();

    private int Count(LintSeverity severity) => Findings.Count(f => f.Severity == severity);
}
