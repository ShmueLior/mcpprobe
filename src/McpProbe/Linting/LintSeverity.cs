namespace McpProbe.Linting;

/// <summary>How serious a lint finding is. Ordered, so <c>Error &gt; Warning &gt; Info</c>.</summary>
public enum LintSeverity
{
    /// <summary>The rule is turned off.</summary>
    None = 0,

    /// <summary>A suggestion.</summary>
    Info = 1,

    /// <summary>Likely to hurt how well models use the tool.</summary>
    Warning = 2,

    /// <summary>Breaks a rule from the MCP specification or makes the tool unusable for models.</summary>
    Error = 3,
}
