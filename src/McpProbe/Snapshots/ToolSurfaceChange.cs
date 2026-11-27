using McpProbe.Internal;

namespace McpProbe.Snapshots;

/// <summary>Whether a change can break existing clients.</summary>
public enum ChangeSeverity
{
    /// <summary>Existing clients keep working.</summary>
    NonBreaking = 0,

    /// <summary>Existing clients or prompts that rely on the old contract can fail.</summary>
    Breaking = 1,
}

/// <summary>The kind of change between two tool surfaces.</summary>
public enum ToolSurfaceChangeKind
{
    /// <summary>A tool was added.</summary>
    ToolAdded,

    /// <summary>A tool was removed.</summary>
    ToolRemoved,

    /// <summary>A tool's title changed.</summary>
    TitleChanged,

    /// <summary>A tool's or property's description changed.</summary>
    DescriptionChanged,

    /// <summary>A tool's behavior hints changed.</summary>
    AnnotationsChanged,

    /// <summary>A parameter or output property was added.</summary>
    PropertyAdded,

    /// <summary>A parameter or output property was removed.</summary>
    PropertyRemoved,

    /// <summary>A parameter or output property became required.</summary>
    BecameRequired,

    /// <summary>A parameter or output property became optional.</summary>
    BecameOptional,

    /// <summary>The JSON type of a schema changed.</summary>
    TypeChanged,

    /// <summary>The allowed values (<c>enum</c>) of a schema changed.</summary>
    EnumChanged,

    /// <summary>A tool started declaring an output schema.</summary>
    OutputSchemaAdded,

    /// <summary>A tool stopped declaring an output schema.</summary>
    OutputSchemaRemoved,

    /// <summary>Another JSON Schema keyword changed. McpProbe 0.1 does not classify these; review them by hand.</summary>
    KeywordChanged,
}

/// <summary>One difference between a baseline and a current tool surface.</summary>
/// <param name="ToolName">The tool the change belongs to.</param>
/// <param name="Path">Where in the tool the change is, for example <c>input.city</c>, <c>input.tags[]</c> or <c>output.temperature</c>; <see langword="null"/> for tool-level changes.</param>
/// <param name="Kind">What changed.</param>
/// <param name="Severity">Whether the change can break clients.</param>
/// <param name="Message">A one-line, human-readable description.</param>
public sealed record ToolSurfaceChange(string ToolName, string? Path, ToolSurfaceChangeKind Kind, ChangeSeverity Severity, string Message)
{
    /// <summary>Gets whether the change can break clients.</summary>
    public bool IsBreaking => Severity == ChangeSeverity.Breaking;

    /// <inheritdoc />
    public override string ToString()
    {
        var text = $"{Printable.Text(ToolName, 100)}: {Printable.Text(Message, 500)}";
        return Path is null ? text : $"{text} [{Printable.Text(Path, 200)}]";
    }
}
