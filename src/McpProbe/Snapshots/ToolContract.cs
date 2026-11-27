using System.Text.Json;

namespace McpProbe.Snapshots;

/// <summary>
/// The model-facing contract of one tool: what a client sees in <c>tools/list</c>.
/// </summary>
public sealed class ToolContract
{
    internal ToolContract(string name, string? title, string? description, JsonElement inputSchema, JsonElement? outputSchema, ToolContractAnnotations? annotations)
    {
        Name = name;
        Title = title;
        Description = description;
        InputSchema = inputSchema;
        OutputSchema = outputSchema;
        Annotations = annotations;
    }

    /// <summary>Gets the tool name.</summary>
    public string Name { get; }

    /// <summary>Gets the display title, if any.</summary>
    public string? Title { get; }

    /// <summary>Gets the description, if any.</summary>
    public string? Description { get; }

    /// <summary>Gets the JSON Schema for the tool's arguments.</summary>
    public JsonElement InputSchema { get; }

    /// <summary>Gets the JSON Schema for the tool's structured output, if the tool declares one.</summary>
    public JsonElement? OutputSchema { get; }

    /// <summary>Gets the behavior hints, if any are set.</summary>
    public ToolContractAnnotations? Annotations { get; }
}

/// <summary>
/// Behavior hints a tool declares (<c>readOnlyHint</c>, <c>destructiveHint</c>, <c>idempotentHint</c>, <c>openWorldHint</c>).
/// </summary>
/// <param name="ReadOnlyHint">Whether the tool only reads.</param>
/// <param name="DestructiveHint">Whether the tool may destroy data.</param>
/// <param name="IdempotentHint">Whether repeated calls with the same arguments have no extra effect.</param>
/// <param name="OpenWorldHint">Whether the tool talks to an open world of external entities.</param>
public sealed record ToolContractAnnotations(bool? ReadOnlyHint, bool? DestructiveHint, bool? IdempotentHint, bool? OpenWorldHint);
