using McpProbe.Snapshots;
using static McpProbe.Tests.TestSupport.SchemaTools;

namespace McpProbe.Tests;

/// <summary>Bounds, multipleOf and pattern: inputs may get looser but not tighter, outputs the other way round.</summary>
public sealed class ToolSurfaceComparerConstraintTests
{
    // A parameter (or output property) 'p' with the given extra keywords, for example "\"minimum\":1".
    private static string Param(string keywords) =>
        "{\"type\":\"object\",\"properties\":{\"p\":{\"type\":\"number\"" + (keywords.Length == 0 ? "" : "," + keywords) + "}}}";

    private static ToolSurfaceChange Input(string before, string after) =>
        Assert.Single(ToolSurfaceComparer.Compare(Surface(Tool("t", Param(before))), Surface(Tool("t", Param(after)))).Changes);

    private static ToolSurfaceChange Output(string before, string after) =>
        Assert.Single(ToolSurfaceComparer.Compare(Surface(Tool("t", outputSchema: Param(before))), Surface(Tool("t", outputSchema: Param(after)))).Changes);

    [Theory]
    [InlineData("minimum", "1", "2")]
    [InlineData("exclusiveMinimum", "0", "0.5")]
    [InlineData("minLength", "1", "3")]
    [InlineData("minItems", "0", "1")]
    [InlineData("minProperties", "1", "2")]
    [InlineData("maximum", "10", "5")]
    [InlineData("exclusiveMaximum", "1e3", "999")]
    [InlineData("maxLength", "20", "19")]
    [InlineData("maxItems", "5", "1")]
    [InlineData("maxProperties", "3", "2")]
    public void Bounds_TighterBreaksInputs_LooserBreaksOutputs(string keyword, string loose, string tight)
    {
        var looseKeywords = $"\"{keyword}\":{loose}";
        var tightKeywords = $"\"{keyword}\":{tight}";

        var tightened = Input(looseKeywords, tightKeywords);
        Assert.Equal((ToolSurfaceChangeKind.ConstraintChanged, ChangeSeverity.Breaking, "input.p"), (tightened.Kind, tightened.Severity, tightened.Path));
        Assert.Contains($"'{keyword}' on parameter 'p' changed from {loose} to {tight} (tighter)", tightened.Message, StringComparison.Ordinal);
        Assert.Equal(ChangeSeverity.NonBreaking, Input(tightKeywords, looseKeywords).Severity);
        Assert.Equal(ChangeSeverity.Breaking, Input("", tightKeywords).Severity);
        Assert.Equal(ChangeSeverity.NonBreaking, Input(tightKeywords, "").Severity);

        Assert.Equal(ChangeSeverity.NonBreaking, Output(looseKeywords, tightKeywords).Severity);
        Assert.Equal(ChangeSeverity.Breaking, Output(tightKeywords, looseKeywords).Severity);
        Assert.Equal(ChangeSeverity.NonBreaking, Output("", tightKeywords).Severity);
        Assert.Equal(ChangeSeverity.Breaking, Output(tightKeywords, "").Severity);
    }

    [Fact]
    public void SwitchingFromMinimumToExclusiveMinimum_IsBreaking()
    {
        var diff = ToolSurfaceComparer.Compare(Surface(Tool("t", Param("\"minimum\":0"))), Surface(Tool("t", Param("\"exclusiveMinimum\":0"))));

        Assert.True(diff.HasBreakingChanges, diff.ToReport());
        Assert.Equal(2, diff.Changes.Count);
    }

    [Fact]
    public void SameBoundWrittenDifferently_IsNotAChange()
    {
        var diff = ToolSurfaceComparer.Compare(Surface(Tool("t", Param("\"minimum\":1,\"maximum\":1e2"))), Surface(Tool("t", Param("\"minimum\":1.0,\"maximum\":100"))));

        Assert.False(diff.HasChanges, diff.ToReport());
    }

    [Theory]
    [InlineData("2", "4", ChangeSeverity.Breaking)] // every multiple of 4 is a multiple of 2, not the other way round
    [InlineData("4", "2", ChangeSeverity.NonBreaking)]
    [InlineData("0.1", "0.05", ChangeSeverity.NonBreaking)]
    [InlineData("0.05", "0.1", ChangeSeverity.Breaking)]
    [InlineData("3", "2", ChangeSeverity.Breaking)]
    public void MultipleOf_IsLooserOnlyWhenTheOldStepIsAMultipleOfTheNewOne(string before, string after, ChangeSeverity expected)
    {
        var change = Input($"\"multipleOf\":{before}", $"\"multipleOf\":{after}");

        Assert.Equal((ToolSurfaceChangeKind.ConstraintChanged, expected), (change.Kind, change.Severity));
        var output = Output($"\"multipleOf\":{before}", $"\"multipleOf\":{after}");
        var neither = before == "3";
        Assert.Equal(neither || expected == ChangeSeverity.NonBreaking ? ChangeSeverity.Breaking : ChangeSeverity.NonBreaking, output.Severity);
        Assert.Equal(neither, change.Message.Contains("neither tighter nor looser", StringComparison.Ordinal));
        Assert.Equal(ChangeSeverity.Breaking, Input("", $"\"multipleOf\":{after}").Severity);
        Assert.Equal(ChangeSeverity.NonBreaking, Input($"\"multipleOf\":{before}", "").Severity);
    }

    [Fact]
    public void Pattern_AddedOrChangedBreaksInputs_RemovedDoesNot()
    {
        // The SDK writes [RegularExpression("^[a-z]+$")] with '+' escaped as \u002B; that is the same pattern.
        Assert.False(ToolSurfaceComparer.Compare(
            Surface(Tool("t", Param("\"pattern\":\"^[a-z]+$\""))),
            Surface(Tool("t", Param("\"pattern\":\"^[a-z]\\u002B$\"")))).HasChanges);

        var added = Input("", "\"pattern\":\"^[a-z]+$\"");
        Assert.Equal((ToolSurfaceChangeKind.ConstraintChanged, ChangeSeverity.Breaking), (added.Kind, added.Severity));
        Assert.Contains("changed from (unset) to '^[a-z]+$' (tighter)", added.Message, StringComparison.Ordinal);
        Assert.Equal(ChangeSeverity.Breaking, Input("\"pattern\":\"^[a-z]+$\"", "\"pattern\":\"^[a-z0-9]+$\"").Severity);
        // A changed pattern may also return values the old one didn't allow, so it breaks outputs too.
        Assert.Equal(ChangeSeverity.Breaking, Output("\"pattern\":\"^[a-z]+$\"", "\"pattern\":\"^[a-z0-9]+$\"").Severity);
        Assert.Equal(ChangeSeverity.NonBreaking, Input("\"pattern\":\"^[a-z]+$\"", "").Severity);

        Assert.Equal(ChangeSeverity.NonBreaking, Output("", "\"pattern\":\"^[a-z]+$\"").Severity);
        Assert.Equal(ChangeSeverity.Breaking, Output("\"pattern\":\"^[a-z]+$\"", "").Severity);
    }

    [Theory]
    [InlineData("\"exclusiveMinimum\":false", "\"exclusiveMinimum\":true")] // draft 4
    [InlineData("\"minimum\":\"1\"", "\"minimum\":\"2\"")]
    [InlineData("\"minimum\":1", "\"minimum\":\"2\"")]
    [InlineData("\"multipleOf\":0", "\"multipleOf\":2")]
    [InlineData("\"multipleOf\":1e300", "\"multipleOf\":2")]
    [InlineData("\"pattern\":1", "\"pattern\":\"x\"")]
    public void ValuesThatAreNotUsableNumbersOrStrings_StayUnclassified(string before, string after)
    {
        var change = Input(before, after);

        Assert.Equal((ToolSurfaceChangeKind.KeywordChanged, ChangeSeverity.NonBreaking), (change.Kind, change.Severity));
        Assert.Contains("review by hand", change.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstraintsOnNestedPropertiesItemsRootAndRefs()
    {
        static ToolSurfaceDiff Diff(string max) => ToolSurfaceComparer.Compare(
            Surface(Tool("t", Schema("10"))), Surface(Tool("t", Schema(max))));

        static string Schema(string max) =>
            "{\"type\":\"object\",\"maxProperties\":" + max + ",\"properties\":{" +
            "\"o\":{\"type\":\"object\",\"properties\":{\"n\":{\"type\":\"integer\",\"maximum\":" + max + "}}}," +
            "\"tags\":{\"type\":\"array\",\"items\":{\"type\":\"string\",\"maxLength\":" + max + "}}," +
            "\"r\":{\"$ref\":\"#/$defs/d\"}}," +
            "\"$defs\":{\"d\":{\"type\":\"integer\",\"maximum\":" + max + "}}}";

        var diff = Diff("5");

        Assert.Equal(["input", "input.o.n", "input.r", "input.tags[]"], diff.Changes.Select(c => c.Path).Order(StringComparer.Ordinal));
        Assert.All(diff.Changes, c => Assert.Equal((ToolSurfaceChangeKind.ConstraintChanged, ChangeSeverity.Breaking), (c.Kind, c.Severity)));
        Assert.Contains(diff.Changes, c => c.Message.StartsWith("'maxProperties' on the input schema", StringComparison.Ordinal));
        Assert.False(Diff("20").HasBreakingChanges, Diff("20").ToReport());
    }

    [Fact]
    public void SdkRangeAttributeChange_FailsTheSnapshot()
    {
        // What the MCP C# SDK 2.4 emits for [Range(1, 10)] int count, then for [Range(1, 5)].
        var before = """{"type":"object","properties":{"count":{"type":"integer","minimum":1,"maximum":10}},"required":["count"]}""";
        var after = """{"type":"object","properties":{"count":{"type":"integer","minimum":1,"maximum":5}},"required":["count"]}""";

        var diff = ToolSurfaceComparer.Compare(Surface(Tool("t", before)), Surface(Tool("t", after)));

        Assert.True(diff.HasBreakingChanges);
        Assert.Contains("'maximum' on parameter 'count' changed from 10 to 5 (tighter)", diff.ToReport(), StringComparison.Ordinal);
    }
}
