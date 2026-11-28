using System.Globalization;
using System.Text;
using McpProbe.Internal;

namespace McpProbe.Snapshots;

/// <summary>The result of comparing two tool surfaces.</summary>
public sealed class ToolSurfaceDiff
{
    internal ToolSurfaceDiff(IReadOnlyList<ToolSurfaceChange> changes) => Changes = changes;

    /// <summary>Gets every change, breaking first, then by tool name and path.</summary>
    public IReadOnlyList<ToolSurfaceChange> Changes { get; }

    /// <summary>Gets whether anything changed.</summary>
    public bool HasChanges => Changes.Count > 0;

    /// <summary>Gets whether any change can break clients.</summary>
    public bool HasBreakingChanges => Changes.Any(c => c.IsBreaking);

    /// <summary>Gets the breaking changes.</summary>
    public IEnumerable<ToolSurfaceChange> BreakingChanges => Changes.Where(c => c.IsBreaking);

    /// <summary>Gets the non-breaking changes.</summary>
    public IEnumerable<ToolSurfaceChange> NonBreakingChanges => Changes.Where(c => !c.IsBreaking);

    /// <summary>Formats the changes as a short plain-text report.</summary>
    /// <returns>The report, or a one-line "no changes" message.</returns>
    public string ToReport()
    {
        if (!HasChanges)
        {
            return "Tool surface unchanged.";
        }

        var breaking = BreakingChanges.ToList();
        var nonBreaking = NonBreakingChanges.ToList();
        var sb = new StringBuilder();
        sb.Append(Summary(breaking.Count, nonBreaking.Count)).Append('\n');

        if (breaking.Count > 0)
        {
            sb.Append('\n').Append("Breaking:").Append('\n');
            foreach (var change in breaking)
            {
                sb.Append("  - ").Append(change).Append('\n');
            }
        }

        if (nonBreaking.Count > 0)
        {
            sb.Append('\n').Append("Non-breaking:").Append('\n');
            foreach (var change in nonBreaking)
            {
                sb.Append("  ~ ").Append(change).Append('\n');
            }
        }

        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// Formats the changes as a complete, self-contained HTML document: inline CSS, no scripts and no external assets.
    /// Server-supplied text is already sanitized and HTML-encoded, so the result is safe to write to a file.
    /// </summary>
    /// <returns>The HTML document.</returns>
    public string ToHtml()
    {
        var rows = Changes.Select(c => (RowClass: c.IsBreaking ? "breaking" : "non-breaking", Cells: new[]
        {
            c.IsBreaking ? "breaking" : "non-breaking",
            c.Kind.ToString(),
            Printable.Text(c.ToolName, 100),
            c.Path is null ? string.Empty : Printable.Text(c.Path, 200),
            Printable.Text(c.Message, 500),
        }));
        return HtmlReport.Page(
            "McpProbe diff report",
            HasBreakingChanges ? "fail" : HasChanges ? "note" : "pass",
            HasBreakingChanges ? "Breaking changes" : HasChanges ? "Non-breaking changes only" : "Unchanged",
            HasChanges ? Summary(BreakingChanges.Count(), NonBreakingChanges.Count()) : "Tool surface unchanged.",
            ["Impact", "Kind", "Tool", "Path", "Message"],
            rows);
    }

    private static string Summary(int breaking, int nonBreaking) => string.Create(CultureInfo.InvariantCulture,
        $"Tool surface changed: {breaking} breaking, {nonBreaking} non-breaking.");

    /// <inheritdoc />
    public override string ToString() => ToReport();
}
