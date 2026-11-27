using McpProbe.Snapshots;
using McpProbe.Tests.TestSupport;
using ModelContextProtocol.Protocol;
using static McpProbe.Tests.TestSupport.SchemaTools;

namespace McpProbe.Tests;

public sealed class ToolSurfaceTests
{
    [Fact]
    public void ToJson_IsStableRegardlessOfKeyAndToolOrder()
    {
        var a = Surface(
            Tool("b_tool", """{"type":"object","properties":{"z":{"type":"string"},"a":{"type":"integer"}},"required":["z"]}"""),
            Tool("a_tool"));
        var b = Surface(
            Tool("a_tool"),
            Tool("b_tool", """{"required":["z"],"properties":{"a":{"type":"integer"},"z":{"type":"string"}},"type":"object"}"""));

        Assert.Equal(a.ToJson(), b.ToJson());
        Assert.Equal(["a_tool", "b_tool"], a.Tools.Select(t => t.Name));
    }

    [Fact]
    public void ToJson_UsesLfEndsWithNewlineAndKeepsUnicodeReadable()
    {
        var json = Surface(Tool("t", description: "Temperature in °C, it's \"exact\".")).ToJson();

        Assert.DoesNotContain("\r", json, StringComparison.Ordinal);
        Assert.EndsWith("}\n", json, StringComparison.Ordinal);
        Assert.Contains("°C, it's \\\"exact\\\".", json, StringComparison.Ordinal);
        Assert.StartsWith("{\n  \"formatVersion\": 1,", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RoundTripsEverything()
    {
        var tool = new Tool
        {
            Name = "full",
            Title = "Full tool",
            Description = "Has every field set.",
            InputSchema = Parse("""{"type":"object","properties":{"q":{"type":"string","description":"Query."}},"required":["q"]}"""),
            OutputSchema = Parse("""{"type":"object","properties":{"n":{"type":"integer"}}}"""),
            Annotations = new ToolAnnotations { ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false },
        };
        var surface = Surface(tool);

        var parsed = ToolSurface.Parse(surface.ToJson());

        Assert.Equal(surface.ToJson(), parsed.ToJson());
        var contract = Assert.Single(parsed.Tools);
        Assert.Equal("Full tool", contract.Title);
        Assert.Equal(new ToolContractAnnotations(true, false, true, false), contract.Annotations);
        Assert.NotNull(contract.OutputSchema);
        Assert.False(ToolSurfaceComparer.Compare(surface, parsed).HasChanges);
    }

    [Fact]
    public void FromTools_UsesAnnotationTitleAndSkipsEmptyAnnotations()
    {
        var withAnnotationTitle = new Tool { Name = "a", InputSchema = Parse("""{"type":"object"}"""), Annotations = new ToolAnnotations { Title = "From annotations" } };

        var contract = Assert.Single(Surface(withAnnotationTitle).Tools);

        Assert.Equal("From annotations", contract.Title);
        Assert.Null(contract.Annotations);
    }

    [Fact]
    public void FromTools_RejectsDuplicateNames()
    {
        var error = Assert.Throws<ArgumentException>(() => Surface(Tool("same"), Tool("same")));
        Assert.Contains("Duplicate tool name 'same'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json", "not valid JSON")]
    [InlineData("[]", "must be a JSON object")]
    [InlineData("""{"tools":[]}""", "no 'formatVersion'")]
    [InlineData("""{"formatVersion":2,"tools":[]}""", "version 2 is not supported")]
    [InlineData("""{"formatVersion":1}""", "no 'tools' array")]
    [InlineData("""{"formatVersion":1,"tools":[{"description":"x"}]}""", "non-empty 'name'")]
    [InlineData("""{"formatVersion":1,"tools":[{"name":"a"},{"name":"a"}]}""", "Duplicate tool name 'a'")]
    public void Parse_RejectsInvalidSnapshots(string json, string expected)
    {
        var error = Assert.Throws<FormatException>(() => ToolSurface.Parse(json));
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DefaultsMissingInputSchemaToObject()
    {
        var surface = ToolSurface.Parse("""{"formatVersion":1,"tools":[{"name":"a"}]}""");

        Assert.Equal("""{"type":"object"}""", Assert.Single(surface.Tools).InputSchema.GetRawText());
    }
}
