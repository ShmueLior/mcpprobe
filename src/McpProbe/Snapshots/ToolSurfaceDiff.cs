using System.Globalization;
using System.Text;

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
        sb.Append(CultureInfo.InvariantCulture, $"Tool surface changed: {breaking.Count} breaking, {nonBreaking.Count} non-breaking.").Append('\n');

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

    /// <inheritdoc />
    public override string ToString() => ToReport();
}
