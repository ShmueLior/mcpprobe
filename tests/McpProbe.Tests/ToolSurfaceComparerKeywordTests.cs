using McpProbe.Snapshots;
using static McpProbe.Tests.TestSupport.SchemaTools;

namespace McpProbe.Tests;

/// <summary>format, uniqueItems, const and the additional/unevaluated keywords follow the same rule as bounds.</summary>
public sealed class ToolSurfaceComparerKeywordTests
{
    // A parameter (or output property) 'p' with the given keywords, for example "\"format\":\"date\"".
    private static string Param(string keywords) =>
        "{\"type\":\"object\",\"properties\":{\"p\":{" + keywords + "}}}";

    private static ToolSurfaceDiff InputDiff(string before, string after) =>
        ToolSurfaceComparer.Compare(Surface(Tool("t", Param(before))), Surface(Tool("t", Param(after))));

    private static ToolSurfaceDiff OutputDiff(string before, string after) =>
        ToolSurfaceComparer.Compare(Surface(Tool("t", outputSchema: Param(before))), Surface(Tool("t", outputSchema: Param(after))));

    private static ToolSurfaceChange Input(string before, string after) => Assert.Single(InputDiff(before, after).Changes);

    private static ToolSurfaceChange Output(string before, string after) => Assert.Single(OutputDiff(before, after).Changes);

    // Checks a tighter change both ways on both sides, plus the message direction.
    private static void AssertTighter(string loose, string tight)
    {
        var change = Input(loose, tight);
        Assert.Equal((ToolSurfaceChangeKind.ConstraintChanged, ChangeSeverity.Breaking, "input.p"), (change.Kind, change.Severity, change.Path));
        Assert.EndsWith("(tighter).", change.Message, StringComparison.Ordinal);
        Assert.Equal((ToolSurfaceChangeKind.ConstraintChanged, ChangeSeverity.NonBreaking), (Input(tight, loose).Kind, Input(tight, loose).Severity));
        Assert.EndsWith("(looser).", Input(tight, loose).Message, StringComparison.Ordinal);
        Assert.Equal(ChangeSeverity.NonBreaking, Output(loose, tight).Severity);
        Assert.Equal(ChangeSeverity.Breaking, Output(tight, loose).Severity);
    }

    private static void AssertNeither(string before, string after)
    {
        var change = Input(before, after);
        Assert.Equal((ToolSurfaceChangeKind.ConstraintChanged, ChangeSeverity.Breaking), (change.Kind, change.Severity));
        Assert.EndsWith("(neither tighter nor looser).", change.Message, StringComparison.Ordinal);
        Assert.Equal(ChangeSeverity.Breaking, Output(before, after).Severity);
    }

    private static void AssertNoChange(string before, string after)
    {
        Assert.False(InputDiff(before, after).HasChanges, InputDiff(before, after).ToReport());
        Assert.False(OutputDiff(before, after).HasChanges, OutputDiff(before, after).ToReport());
    }

    private static void AssertUnclassified(string before, string after)
    {
        var change = Input(before, after);
        Assert.Equal((ToolSurfaceChangeKind.KeywordChanged, ChangeSeverity.NonBreaking), (change.Kind, change.Severity));
        Assert.Contains("review by hand", change.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_AddedIsTighter_ChangedIsNeither()
    {
        AssertTighter("\"type\":\"string\"", "\"type\":\"string\",\"format\":\"date\"");
        AssertNeither("\"format\":\"date\"", "\"format\":\"date-time\"");
        AssertNeither("\"format\":\"email\"", "\"format\":\"uri\"");
        AssertNoChange("\"format\":\"date\"", "\"format\":\"date\"");
        Assert.Equal("'format' on parameter 'p' changed from 'date' to 'date-time' (neither tighter nor looser).", Input("\"format\":\"date\"", "\"format\":\"date-time\"").Message);
        AssertUnclassified("\"format\":1", "\"format\":\"date\"");
        // A JSON null reads as a missing value, the same as for 'pattern'.
        Assert.EndsWith("(looser).", Input("\"format\":\"date\"", "\"format\":null").Message, StringComparison.Ordinal);

        var longFormat = new string('x', 500);
        Assert.Contains("'" + new string('x', 57) + "...'", Input("", $"\"format\":\"{longFormat}\"").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UniqueItems_MissingIsFalse_TrueIsTighter()
    {
        AssertNoChange("\"type\":\"array\"", "\"type\":\"array\",\"uniqueItems\":false");
        AssertNoChange("\"uniqueItems\":true", "\"uniqueItems\":true");
        AssertTighter("\"type\":\"array\"", "\"type\":\"array\",\"uniqueItems\":true");
        AssertTighter("\"uniqueItems\":false", "\"uniqueItems\":true");
        AssertUnclassified("\"uniqueItems\":1", "\"uniqueItems\":true");
        AssertUnclassified("\"uniqueItems\":false", "\"uniqueItems\":\"true\"");
    }

    [Theory]
    [InlineData("1", "2")]
    [InlineData("\"a\"", "\"b\"")]
    [InlineData("1", "\"1\"")]
    [InlineData("null", "false")]
    [InlineData("{\"a\":1}", "{\"a\":2}")]
    [InlineData("[1,2]", "[2,1]")]
    public void Const_AddedIsTighter_ChangedIsNeither(string before, string after)
    {
        AssertTighter("", $"\"const\":{before}");
        AssertNeither($"\"const\":{before}", $"\"const\":{after}");
        AssertNoChange($"\"const\":{before}", $"\"const\":{before}");
    }

    [Fact]
    public void Const_Messages_ShowNullAndTruncateLongValues()
    {
        Assert.Equal("'const' on parameter 'p' changed from (unset) to null (tighter).", Input("", "\"const\":null").Message);
        Assert.Equal("'const' on parameter 'p' changed from 1 to '1' (neither tighter nor looser).", Input("\"const\":1", "\"const\":\"1\"").Message);
        Assert.Equal("'const' on parameter 'p' changed from null to (unset) (looser).", Input("\"const\":null", "").Message);

        var big = "{" + string.Join(",", Enumerable.Range(0, 100).Select(i => $"\"k{i}\":{i}")) + "}";
        var change = Input("", $"\"const\":{big}");
        Assert.Contains("...", change.Message, StringComparison.Ordinal);
        Assert.True(change.Message.Length < 200, change.Message);
    }

    [Fact]
    public void Const_AndEnum_AreClassifiedSeparately()
    {
        var diff = InputDiff("\"enum\":[1,2],\"const\":1", "\"enum\":[1],\"const\":2");

        Assert.Equal(2, diff.Changes.Count);
        Assert.Contains(diff.Changes, c => c.Kind == ToolSurfaceChangeKind.EnumChanged && c.IsBreaking);
        Assert.Contains(diff.Changes, c => c.Kind == ToolSurfaceChangeKind.ConstraintChanged && c.IsBreaking && c.Message.StartsWith("'const'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("additionalProperties")]
    [InlineData("unevaluatedProperties")]
    [InlineData("additionalItems")]
    [InlineData("unevaluatedItems")]
    public void ExtrasKeywords_MissingTrueAndEmptyAllowAll_SchemaIsTighter_FalseIsTightest(string keyword)
    {
        var none = "\"type\":\"object\"";
        var allowAll = $"\"{keyword}\":true";
        var empty = $"\"{keyword}\":{{}}";
        var schema = $"\"{keyword}\":{{\"type\":\"string\"}}";
        var forbid = $"\"{keyword}\":false";

        AssertNoChange(none, none + "," + allowAll);
        AssertNoChange(allowAll, empty);
        AssertNoChange(none, none + "," + empty);
        AssertNoChange(forbid, forbid);

        AssertTighter(none, none + "," + forbid);
        AssertTighter(allowAll, forbid);
        AssertTighter(allowAll, schema);
        // {} and a schema are both schemas, so they're compared as schemas: {} has no type, the new one does.
        var walked = Input(empty, schema);
        Assert.Equal(($"input.p[{keyword}]", ToolSurfaceChangeKind.TypeChanged, ChangeSeverity.Breaking), (walked.Path, walked.Kind, walked.Severity));
        AssertTighter(schema, forbid);
        Assert.Equal($"'{keyword}' on parameter 'p' changed from true to false (tighter).", Input(allowAll, forbid).Message);

        AssertUnclassified(forbid, $"\"{keyword}\":1");
        Assert.EndsWith("(looser).", Input(forbid, $"\"{keyword}\":null").Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("additionalProperties")]
    [InlineData("unevaluatedItems")]
    public void ExtrasSchemas_OnBothSides_AreComparedLikeAnyOtherSchema(string keyword)
    {
        static string Extras(string keyword, string inner) =>
            "\"type\":\"object\",\"" + keyword + "\":{\"type\":\"object\",\"properties\":{\"zip\":{\"type\":\"string\"" + inner + "}}}";

        AssertNoChange(Extras(keyword, ""), Extras(keyword, ""));

        var tighter = Input(Extras(keyword, ",\"maxLength\":10"), Extras(keyword, ",\"maxLength\":5"));
        Assert.Equal(($"input.p[{keyword}].zip", ToolSurfaceChangeKind.ConstraintChanged, ChangeSeverity.Breaking), (tighter.Path, tighter.Kind, tighter.Severity));
        Assert.Equal(ChangeSeverity.NonBreaking, Output(Extras(keyword, ",\"maxLength\":10"), Extras(keyword, ",\"maxLength\":5")).Severity);

        var type = Input(Extras(keyword, ""), Extras(keyword, "").Replace("\"string\"", "\"integer\"", StringComparison.Ordinal));
        Assert.Equal(($"input.p[{keyword}].zip", ToolSurfaceChangeKind.TypeChanged, ChangeSeverity.Breaking), (type.Path, type.Kind, type.Severity));

        var required = Input(Extras(keyword, ""), Extras(keyword, "").Replace("}}}", "}},\"required\":[\"zip\"]}", StringComparison.Ordinal));
        Assert.Equal(($"input.p[{keyword}].zip", ToolSurfaceChangeKind.BecameRequired, ChangeSeverity.Breaking), (required.Path, required.Kind, required.Severity));
    }

    [Fact]
    public void Extras_AtTheRoot_AndThroughALocalRef()
    {
        var root = ToolSurfaceComparer.Compare(
            Surface(Tool("t", """{"type":"object","additionalProperties":{"type":"string","maxLength":10}}""")),
            Surface(Tool("t", """{"type":"object","additionalProperties":{"type":"string","maxLength":5}}""")));
        var change = Assert.Single(root.Changes);
        Assert.Equal(("input[additionalProperties]", ChangeSeverity.Breaking), (change.Path, change.Severity));

        var closed = ToolSurfaceComparer.Compare(
            Surface(Tool("t", """{"type":"object","properties":{"a":{"type":"string"}}}""")),
            Surface(Tool("t", """{"type":"object","properties":{"a":{"type":"string"}},"additionalProperties":false}""")));
        var closedChange = Assert.Single(closed.Changes);
        Assert.Equal(("input", ChangeSeverity.Breaking), (closedChange.Path, closedChange.Severity));
        Assert.StartsWith("'additionalProperties' on the input schema", closedChange.Message, StringComparison.Ordinal);

        static string WithRef(string extras) =>
            "{\"type\":\"object\",\"properties\":{\"home\":{\"$ref\":\"#/$defs/a\"}},\"$defs\":{\"a\":{\"type\":\"object\",\"format\":\"x\"," + extras + "}}}";

        var viaRef = ToolSurfaceComparer.Compare(
            Surface(Tool("t", WithRef("\"additionalProperties\":{\"$ref\":\"#/$defs/s\"}},\"s\":{\"type\":\"string\""))),
            Surface(Tool("t", WithRef("\"additionalProperties\":{\"$ref\":\"#/$defs/s\"}},\"s\":{\"type\":\"integer\""))));
        var refChange = Assert.Single(viaRef.Changes);
        Assert.Equal(("input.home[additionalProperties]", ToolSurfaceChangeKind.TypeChanged, ChangeSeverity.Breaking), (refChange.Path, refChange.Kind, refChange.Severity));

        var uniqueViaRef = ToolSurfaceComparer.Compare(
            Surface(Tool("t", WithRef("\"uniqueItems\":false"))),
            Surface(Tool("t", WithRef("\"uniqueItems\":true"))));
        var uniqueChange = Assert.Single(uniqueViaRef.Changes);
        Assert.Equal(("input.home", ToolSurfaceChangeKind.ConstraintChanged, ChangeSeverity.Breaking), (uniqueChange.Path, uniqueChange.Kind, uniqueChange.Severity));
    }

    [Fact]
    public void Combinators_StayUnclassified()
    {
        AssertUnclassified("\"allOf\":[{\"minLength\":1}]", "\"allOf\":[{\"minLength\":2}]");
        AssertUnclassified("\"anyOf\":[{\"type\":\"string\"}]", "\"anyOf\":[{\"type\":\"string\"},{\"type\":\"null\"}]");
    }
}
