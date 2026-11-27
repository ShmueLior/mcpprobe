using System.Text.Json;
using McpProbe.Snapshots;
using ModelContextProtocol.Protocol;

namespace McpProbe.Tests.TestSupport;

/// <summary>Builds protocol tools and surfaces from inline JSON, so comparer tests read like the schemas they check.</summary>
internal static class SchemaTools
{
    public static Tool Tool(string name, string inputSchema = """{"type":"object"}""", string? description = "A test tool that does something useful.", string? outputSchema = null) =>
        new()
        {
            Name = name,
            Description = description,
            InputSchema = Parse(inputSchema),
            OutputSchema = outputSchema is null ? null : Parse(outputSchema),
        };

    public static ToolSurface Surface(params Tool[] tools) => ToolSurface.FromTools(tools);

    public static ToolSurfaceDiff Diff(Tool before, Tool after) =>
        ToolSurfaceComparer.Compare(Surface(before), Surface(after));

    public static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
