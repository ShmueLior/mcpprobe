using McpProbe.Snapshots;
using static McpProbe.Tests.TestSupport.SchemaTools;

namespace McpProbe.Tests;

public sealed class ToolSurfaceComparerTests
{
    private const string CityRequired = """{"type":"object","properties":{"city":{"type":"string","description":"City."}},"required":["city"]}""";

    private static ToolSurfaceChange Single(ToolSurfaceDiff diff) => Assert.Single(diff.Changes);

    [Fact]
    public void IdenticalSurfaces_HaveNoChanges()
    {
        var diff = Diff(Tool("t", CityRequired), Tool("t", CityRequired));

        Assert.False(diff.HasChanges);
        Assert.False(diff.HasBreakingChanges);
        Assert.Equal("Tool surface unchanged.", diff.ToReport());
    }

    [Fact]
    public void RemovedTool_IsBreaking_AddedTool_IsNot()
    {
        var diff = ToolSurfaceComparer.Compare(Surface(Tool("old")), Surface(Tool("new")));

        Assert.Collection(diff.Changes,
            c => Assert.Equal((ToolSurfaceChangeKind.ToolRemoved, ChangeSeverity.Breaking, "old"), (c.Kind, c.Severity, c.ToolName)),
            c => Assert.Equal((ToolSurfaceChangeKind.ToolAdded, ChangeSeverity.NonBreaking, "new"), (c.Kind, c.Severity, c.ToolName)));
    }

    [Fact]
    public void DescriptionTitleAndAnnotationChanges_AreNonBreaking()
    {
        var before = Tool("t", description: "Old description for the tool.");
        var after = Tool("t", description: "New description for the tool.");
        after.Title = "Shiny";
        after.Annotations = new() { ReadOnlyHint = true };

        var diff = Diff(before, after);

        Assert.False(diff.HasBreakingChanges);
        Assert.Equal(
            [ToolSurfaceChangeKind.TitleChanged, ToolSurfaceChangeKind.DescriptionChanged, ToolSurfaceChangeKind.AnnotationsChanged],
            diff.Changes.Select(c => c.Kind).Order());
        Assert.Contains(diff.Changes, c => c.Message == "Annotation 'readOnlyHint' changed from unset to true.");
    }

    [Fact]
    public void RemovedParameter_IsBreaking_WithRenameHintWhenUnambiguous()
    {
        var after = """{"type":"object","properties":{"location":{"type":"string"},"days":{"type":"integer"}},"required":["location"]}""";

        var diff = Diff(Tool("t", CityRequired), Tool("t", after));

        var removed = Assert.Single(diff.BreakingChanges, c => c.Kind == ToolSurfaceChangeKind.PropertyRemoved);
        Assert.Equal("input.city", removed.Path);
        Assert.Equal("Parameter 'city' was removed (possibly renamed to 'location').", removed.Message);
        Assert.Contains(diff.BreakingChanges, c => c.Kind == ToolSurfaceChangeKind.PropertyAdded && c.Path == "input.location");
        Assert.Contains(diff.NonBreakingChanges, c => c.Kind == ToolSurfaceChangeKind.PropertyAdded && c.Path == "input.days");
    }

    [Fact]
    public void RemovedParameter_HasNoRenameHintWhenAmbiguous()
    {
        var after = """{"type":"object","properties":{"town":{"type":"string"},"place":{"type":"string"}}}""";

        var removed = Assert.Single(Diff(Tool("t", CityRequired), Tool("t", after)).Changes, c => c.Kind == ToolSurfaceChangeKind.PropertyRemoved);

        Assert.Equal("Parameter 'city' was removed.", removed.Message);
    }

    [Theory]
    [InlineData(true, ChangeSeverity.Breaking, "Parameter 'units' was added (required).")]
    [InlineData(false, ChangeSeverity.NonBreaking, "Parameter 'units' was added (optional).")]
    public void AddedParameter_IsBreakingOnlyWhenRequired(bool required, ChangeSeverity expected, string message)
    {
        var requiredList = required ? "\"city\",\"units\"" : "\"city\"";
        var after = """{"type":"object","properties":{"city":{"type":"string","description":"City."},"units":{"type":"string"}},"required":[""" + requiredList + "]}";

        var change = Single(Diff(Tool("t", CityRequired), Tool("t", after)));

        Assert.Equal((ToolSurfaceChangeKind.PropertyAdded, expected, message), (change.Kind, change.Severity, change.Message));
    }

    [Fact]
    public void BecomingRequired_IsBreaking_BecomingOptional_IsNot()
    {
        var optional = """{"type":"object","properties":{"city":{"type":"string","description":"City."}}}""";

        var tightened = Single(Diff(Tool("t", optional), Tool("t", CityRequired)));
        var loosened = Single(Diff(Tool("t", CityRequired), Tool("t", optional)));

        Assert.Equal((ToolSurfaceChangeKind.BecameRequired, ChangeSeverity.Breaking), (tightened.Kind, tightened.Severity));
        Assert.Equal((ToolSurfaceChangeKind.BecameOptional, ChangeSeverity.NonBreaking), (loosened.Kind, loosened.Severity));
    }

    [Theory]
    [InlineData("\"string\"", "\"integer\"", ChangeSeverity.Breaking, "string to integer")]
    [InlineData("\"integer\"", "\"number\"", ChangeSeverity.NonBreaking, "integer to number")]
    [InlineData("\"number\"", "\"integer\"", ChangeSeverity.Breaking, "number to integer")]
    [InlineData("\"string\"", "[\"string\",\"null\"]", ChangeSeverity.NonBreaking, "string to null | string")]
    [InlineData("[\"string\",\"null\"]", "\"string\"", ChangeSeverity.Breaking, "null | string to string")]
    [InlineData(null, "\"string\"", ChangeSeverity.Breaking, "any to string")]
    [InlineData("\"string\"", null, ChangeSeverity.NonBreaking, "string to any")]
    public void InputTypeChanges_MayWidenButNotNarrow(string? before, string? after, ChangeSeverity expected, string message)
    {
        static string Schema(string? type) =>
            type is null ? """{"type":"object","properties":{"v":{}}}""" : """{"type":"object","properties":{"v":{"type":""" + type + "}}}";

        var change = Single(Diff(Tool("t", Schema(before)), Tool("t", Schema(after))));

        Assert.Equal(ToolSurfaceChangeKind.TypeChanged, change.Kind);
        Assert.Equal(expected, change.Severity);
        Assert.Equal($"Parameter 'v' type changed from {message}.", change.Message);
        Assert.Equal("input.v", change.Path);
    }

    [Fact]
    public void EnumChanges_OnInputs()
    {
        static string Schema(string? values) =>
            values is null ? """{"type":"object","properties":{"u":{"type":"string"}}}""" : """{"type":"object","properties":{"u":{"type":"string","enum":[""" + values + "]}}}";

        var removed = Diff(Tool("t", Schema("\"c\",\"f\",\"k\"")), Tool("t", Schema("\"c\",\"f\"")));
        var added = Diff(Tool("t", Schema("\"c\",\"f\"")), Tool("t", Schema("\"c\",\"f\",\"k\"")));
        var introduced = Diff(Tool("t", Schema(null)), Tool("t", Schema("\"c\"")));
        var dropped = Diff(Tool("t", Schema("\"c\"")), Tool("t", Schema(null)));

        Assert.Equal((ChangeSeverity.Breaking, "Allowed values removed from parameter 'u': \"k\"."), (Single(removed).Severity, Single(removed).Message));
        Assert.Equal(ChangeSeverity.NonBreaking, Single(added).Severity);
        Assert.Equal(ChangeSeverity.Breaking, Single(introduced).Severity);
        Assert.Equal(ChangeSeverity.NonBreaking, Single(dropped).Severity);
        Assert.All([removed, added, introduced, dropped], d => Assert.Equal(ToolSurfaceChangeKind.EnumChanged, Single(d).Kind));
    }

    [Fact]
    public void NestedObjectsAndArrayItems_AreCompared()
    {
        var before = """
            {"type":"object","properties":{
              "address":{"type":"object","properties":{"zip":{"type":"string"},"street":{"type":"string"}},"required":["zip"]},
              "tags":{"type":"array","items":{"type":"string"}}}}
            """;
        var after = """
            {"type":"object","properties":{
              "address":{"type":"object","properties":{"street":{"type":"string"}},"required":["street"]},
              "tags":{"type":"array","items":{"type":"integer"}}}}
            """;

        var diff = Diff(Tool("t", before), Tool("t", after));

        Assert.Contains(diff.BreakingChanges, c => c.Path == "input.address.zip" && c.Kind == ToolSurfaceChangeKind.PropertyRemoved && c.Message == "Property 'address.zip' was removed.");
        Assert.Contains(diff.BreakingChanges, c => c.Path == "input.address.street" && c.Kind == ToolSurfaceChangeKind.BecameRequired);
        Assert.Contains(diff.BreakingChanges, c => c.Path == "input.tags[]" && c.Kind == ToolSurfaceChangeKind.TypeChanged);
    }

    [Fact]
    public void OutputSchema_RulesAreReversed()
    {
        const string before = """{"type":"object","properties":{"temp":{"type":"integer"},"summary":{"type":"string"},"wind":{"type":"number"}},"required":["temp","summary"]}""";
        const string after = """{"type":"object","properties":{"temp":{"type":"number"},"wind":{"type":"integer"},"humidity":{"type":"integer"}},"required":["wind"]}""";

        var diff = Diff(Tool("t", outputSchema: before), Tool("t", outputSchema: after));

        Assert.Contains(diff.BreakingChanges, c => c.Path == "output.summary" && c.Kind == ToolSurfaceChangeKind.PropertyRemoved && c.Message == "Output property 'summary' was removed.");
        Assert.Contains(diff.BreakingChanges, c => c.Path == "output.temp" && c.Kind == ToolSurfaceChangeKind.BecameOptional);
        Assert.Contains(diff.BreakingChanges, c => c.Path == "output.temp" && c.Kind == ToolSurfaceChangeKind.TypeChanged);
        Assert.Contains(diff.NonBreakingChanges, c => c.Path == "output.wind" && c.Kind == ToolSurfaceChangeKind.TypeChanged);
        Assert.Contains(diff.NonBreakingChanges, c => c.Path == "output.wind" && c.Kind == ToolSurfaceChangeKind.BecameRequired);
        Assert.Contains(diff.NonBreakingChanges, c => c.Path == "output.humidity" && c.Kind == ToolSurfaceChangeKind.PropertyAdded);
    }

    [Fact]
    public void OutputEnumGainingValues_IsBreaking()
    {
        var diff = Diff(
            Tool("t", outputSchema: """{"type":"object","properties":{"s":{"type":"string","enum":["ok"]}}}"""),
            Tool("t", outputSchema: """{"type":"object","properties":{"s":{"type":"string","enum":["ok","partial"]}}}"""));

        Assert.Equal(ChangeSeverity.Breaking, Single(diff).Severity);
    }

    [Fact]
    public void OutputSchemaRemoved_IsBreaking_Added_IsNot()
    {
        const string output = """{"type":"object"}""";

        Assert.Equal((ToolSurfaceChangeKind.OutputSchemaRemoved, ChangeSeverity.Breaking),
            (Single(Diff(Tool("t", outputSchema: output), Tool("t"))).Kind, Single(Diff(Tool("t", outputSchema: output), Tool("t"))).Severity));
        Assert.Equal((ToolSurfaceChangeKind.OutputSchemaAdded, ChangeSeverity.NonBreaking),
            (Single(Diff(Tool("t"), Tool("t", outputSchema: output))).Kind, Single(Diff(Tool("t"), Tool("t", outputSchema: output))).Severity));
    }

    [Fact]
    public void OtherKeywords_AreReportedButNotClassified()
    {
        var diff = Diff(
            Tool("t", """{"type":"object","properties":{"days":{"type":"string","allOf":[{"minLength":1}],"default":3}}}"""),
            Tool("t", """{"type":"object","properties":{"days":{"type":"string","allOf":[{"minLength":2}]}}}"""));

        Assert.False(diff.HasBreakingChanges);
        Assert.Equal(2, diff.Changes.Count);
        Assert.All(diff.Changes, c => Assert.Equal(ToolSurfaceChangeKind.KeywordChanged, c.Kind));
        Assert.Contains(diff.Changes, c => c.Message == "Keyword 'allOf' on parameter 'days' changed from [{\"minLength\":1}] to [{\"minLength\":2}] (not classified, review by hand).");
        Assert.Contains(diff.Changes, c => c.Message.Contains("'default'", StringComparison.Ordinal) && c.Message.Contains("to (unset)", StringComparison.Ordinal));
    }

    [Fact]
    public void ParameterDescriptionChange_IsNonBreaking()
    {
        var after = CityRequired.Replace("\"City.\"", "\"City name.\"", StringComparison.Ordinal);

        var change = Single(Diff(Tool("t", CityRequired), Tool("t", after)));

        Assert.Equal((ToolSurfaceChangeKind.DescriptionChanged, ChangeSeverity.NonBreaking, "Description of parameter 'city' changed."), (change.Kind, change.Severity, change.Message));
    }

    [Fact]
    public void Report_ListsBreakingFirstWithPaths()
    {
        var diff = ToolSurfaceComparer.Compare(
            Surface(Tool("a_tool"), Tool("z_tool", CityRequired)),
            Surface(Tool("z_tool", """{"type":"object"}"""), Tool("b_tool")));

        Assert.Equal(
            """
            Tool surface changed: 2 breaking, 1 non-breaking.

            Breaking:
              - a_tool: Tool was removed.
              - z_tool: Parameter 'city' was removed. [input.city]

            Non-breaking:
              ~ b_tool: Tool was added.
            """.ReplaceLineEndings("\n"),
            diff.ToReport());
        Assert.Equal(diff.ToReport(), diff.ToString());
    }

    [Fact]
    public void LongKeywordValues_AreTruncated()
    {
        var longPattern = new string('a', 200);
        var diff = Diff(
            Tool("t", "{\"type\":\"object\",\"properties\":{\"p\":{\"type\":\"string\",\"pattern\":\"" + longPattern + "\"}}}"),
            Tool("t", """{"type":"object","properties":{"p":{"type":"string"}}}"""));

        Assert.True(Single(diff).Message.Length < 200);
        Assert.Contains("...", Single(diff).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => ToolSurfaceComparer.Compare(null!, Surface()));
        Assert.Throws<ArgumentNullException>(() => ToolSurfaceComparer.Compare(Surface(), null!));
    }
}
