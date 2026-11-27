using McpProbe.Snapshots;
using static McpProbe.Tests.TestSupport.SchemaTools;

namespace McpProbe.Tests;

/// <summary>Local <c>$ref</c>s are followed, so changes inside referenced schemas are classified like inline ones.</summary>
public sealed class ToolSurfaceComparerRefTests
{
    private static ToolSurfaceDiff Compare(string before, string after) =>
        ToolSurfaceComparer.Compare(Surface(Tool("t", before)), Surface(Tool("t", after)));

    private static ToolSurfaceDiff CompareOutput(string before, string after) =>
        ToolSurfaceComparer.Compare(Surface(Tool("t", outputSchema: before)), Surface(Tool("t", outputSchema: after)));

    private static string WithDef(string defsKeyword, string zip) =>
        $$$$"""{"type":"object","properties":{"home":{"$ref":"#/{{{{defsKeyword}}}}/address"}},"{{{{defsKeyword}}}}":{"address":{"type":"object","properties":{"zip":{{{{zip}}}}},"required":["zip"]}}}""";

    [Theory]
    [InlineData("$defs")]
    [InlineData("definitions")]
    public void ChangeInsideReferencedSchema_IsClassifiedAtTheUsePath(string defsKeyword)
    {
        var diff = Compare(WithDef(defsKeyword, """{"type":"number"}"""), WithDef(defsKeyword, """{"type":"integer"}"""));

        var change = Assert.Single(diff.Changes);
        Assert.Equal((ToolSurfaceChangeKind.TypeChanged, ChangeSeverity.Breaking, "input.home.zip"), (change.Kind, change.Severity, change.Path));
        Assert.False(Compare(WithDef(defsKeyword, """{"type":"integer"}"""), WithDef(defsKeyword, """{"type":"number"}""")).HasBreakingChanges);
    }

    [Fact]
    public void RemovedPropertyInsideReferencedOutputSchema_IsBreaking()
    {
        var before = """{"type":"object","properties":{"forecast":{"$ref":"#/$defs/f"}},"$defs":{"f":{"type":"object","properties":{"high":{"type":"number"},"low":{"type":"number"}}}}}""";
        var after = """{"type":"object","properties":{"forecast":{"$ref":"#/$defs/f"}},"$defs":{"f":{"type":"object","properties":{"high":{"type":"number"}}}}}""";

        var change = Assert.Single(CompareOutput(before, after).Changes);
        Assert.Equal((ToolSurfaceChangeKind.PropertyRemoved, ChangeSeverity.Breaking, "output.forecast.low"), (change.Kind, change.Severity, change.Path));
    }

    [Fact]
    public void MovingASchemaIntoDefs_OrRenamingTheDef_IsNotAChange()
    {
        var inline = """{"type":"object","properties":{"home":{"type":"object","properties":{"zip":{"type":"string"}}}}}""";
        var referenced = """{"type":"object","properties":{"home":{"$ref":"#/$defs/address"}},"$defs":{"address":{"type":"object","properties":{"zip":{"type":"string"}}}}}""";
        var renamed = """{"type":"object","properties":{"home":{"$ref":"#/$defs/addr"}},"$defs":{"addr":{"type":"object","properties":{"zip":{"type":"string"}}}}}""";

        Assert.False(Compare(inline, referenced).HasChanges, Compare(inline, referenced).ToReport());
        Assert.False(Compare(referenced, inline).HasChanges, Compare(referenced, inline).ToReport());
        Assert.False(Compare(referenced, renamed).HasChanges, Compare(referenced, renamed).ToReport());

        var rootRef = """{"type":"object","$ref":"#/$defs/args","$defs":{"args":{"type":"object","properties":{"home":{"$ref":"#/$defs/address"}}},"address":{"type":"object","properties":{"zip":{"type":"string"}}}}}""";
        Assert.False(Compare(inline, rootRef).HasChanges, Compare(inline, rootRef).ToReport());
        Assert.Contains(Compare(rootRef, rootRef.Replace("\"string\"", "\"integer\"", StringComparison.Ordinal)).Changes, c => c.Path == "input.home.zip" && c.IsBreaking);
    }

    [Fact]
    public void UnusedDefinitionChanges_AreIgnoredWhenEveryRefResolves()
    {
        var before = """{"type":"object","properties":{"p":{"type":"string"}},"$defs":{"unused":{"type":"string"}}}""";
        var after = """{"type":"object","properties":{"p":{"type":"string"}},"$defs":{"unused":{"type":"integer"}}}""";

        Assert.False(Compare(before, after).HasChanges);
    }

    [Fact]
    public void RefsTheSdkEmitsForRecursiveTypes_AreFollowed()
    {
        // What the MCP C# SDK 2.4 emits for record Node(string Name, List<Node>? Children, Node? Parent).
        static string Node(string nameType) =>
            """{"type":"object","properties":{"root":{"type":"object","properties":{"name":{"type":"NAME"},"children":{"type":["array","null"],"items":{"$ref":"#/properties/root"}},"parent":{"type":["object","null"],"properties":{"name":{"type":"NAME"},"children":{"$ref":"#/properties/root/properties/children","items":{}},"parent":{"$ref":"#/properties/root/properties/parent"}},"required":["name","children","parent"]}},"required":["name","children","parent"]}},"required":["root"]}"""
                .Replace("NAME", nameType, StringComparison.Ordinal);

        Assert.False(Compare(Node("string"), Node("string")).HasChanges);

        var diff = Compare(Node("number"), Node("integer"));
        Assert.True(diff.HasBreakingChanges, diff.ToReport());
        Assert.All(diff.Changes, c => Assert.Equal(ToolSurfaceChangeKind.TypeChanged, c.Kind));
        Assert.Contains(diff.Changes, c => c.Path == "input.root.name");
        Assert.Contains(diff.Changes, c => c.Path == "input.root.children[].name");
        Assert.Contains(diff.Changes, c => c.Path == "input.root.parent.name");
    }

    [Fact]
    public void SelfAndMutualRecursion_Terminate()
    {
        var self = """{"type":"object","properties":{"name":{"type":"string"},"next":{"$ref":"#"}}}""";
        var selfChanged = """{"type":"object","properties":{"name":{"type":"integer"},"next":{"$ref":"#"}}}""";
        var mutual = """{"type":"object","properties":{"a":{"$ref":"#/$defs/a"}},"$defs":{"a":{"type":"object","properties":{"b":{"$ref":"#/$defs/b"}}},"b":{"type":"object","properties":{"a":{"$ref":"#/$defs/a"},"x":{"type":"string"}}}}}""";
        var mutualChanged = mutual.Replace("""{"type":"string"}""", """{"type":"boolean"}""", StringComparison.Ordinal);

        Assert.False(Compare(self, self).HasChanges);
        Assert.Contains(Compare(self, selfChanged).Changes, c => c.Path == "input.name" && c.IsBreaking);
        Assert.False(Compare(mutual, mutual).HasChanges);
        Assert.Contains(Compare(mutual, mutualChanged).Changes, c => c.Path == "input.a.b.x" && c.IsBreaking);
    }

    [Fact]
    public void RefChains_AreFollowed_AndLoopingChainsStayUnresolved()
    {
        var chain = """{"type":"object","properties":{"p":{"$ref":"#/$defs/alias"}},"$defs":{"alias":{"$ref":"#/$defs/real"},"real":{"type":"string"}}}""";
        var chainChanged = chain.Replace("\"string\"", "\"integer\"", StringComparison.Ordinal);
        var loop = """{"type":"object","properties":{"p":{"$ref":"#/$defs/a"}},"$defs":{"a":{"$ref":"#/$defs/b"},"b":{"$ref":"#/$defs/a"}}}""";
        var loopChanged = loop.Replace("""{"a":{"$ref":"#/$defs/b"}""", """{"a":{"$ref":"#/$defs/b"},"x":1""", StringComparison.Ordinal);

        Assert.Contains(Compare(chain, chainChanged).Changes, c => c.Path == "input.p" && c.Kind == ToolSurfaceChangeKind.TypeChanged && c.IsBreaking);
        Assert.False(Compare(loop, loop).HasChanges);
        var change = Assert.Single(Compare(loop, loopChanged).Changes);
        Assert.Equal((ToolSurfaceChangeKind.KeywordChanged, "input"), (change.Kind, change.Path));
    }

    [Theory]
    [InlineData("https://example.com/address.json")]
    [InlineData("#address")]
    [InlineData("#/$defs/missing")]
    public void UnresolvableRefs_KeepBeingReportedAsKeywords(string reference)
    {
        var before = $$$$"""{"type":"object","properties":{"home":{"$ref":"{{{{reference}}}}"}},"$defs":{"address":{"$anchor":"address","type":"string"}}}""";
        var after = before.Replace("\"string\"", "\"integer\"", StringComparison.Ordinal);
        var otherRef = before.Replace(reference, reference + "2", StringComparison.Ordinal);

        var change = Assert.Single(Compare(before, after).Changes);
        Assert.Equal((ToolSurfaceChangeKind.KeywordChanged, "input", ChangeSeverity.NonBreaking), (change.Kind, change.Path, change.Severity));
        Assert.Contains("'$defs'", change.Message, StringComparison.Ordinal);
        var refChange = Assert.Single(Compare(before, otherRef).Changes);
        Assert.Equal((ToolSurfaceChangeKind.KeywordChanged, "input.home"), (refChange.Kind, refChange.Path));
    }

    [Fact]
    public void SiblingDescription_WinsOverTheTarget_OtherSiblingsOnlyFillGaps()
    {
        static string Schema(string siblings) =>
            $$$$$"""{"type":"object","properties":{"home":{"$ref":"#/$defs/a"{{{{{siblings}}}}}}},"$defs":{"a":{"type":"object","description":"An address.","items":{"type":"string"}}}}""";

        var diff = Compare(Schema(""), Schema(""","description":"Home address." """));
        var change = Assert.Single(diff.Changes);
        Assert.Equal((ToolSurfaceChangeKind.DescriptionChanged, "input.home"), (change.Kind, change.Path));

        // The SDK puts "items":{} next to some refs; it must not hide the referenced items schema.
        Assert.False(Compare(Schema(""), Schema(""","items":{}""")).HasChanges);
        var gap = Assert.Single(Compare(Schema(""), Schema(""","minimum":1""")).Changes);
        Assert.Equal((ToolSurfaceChangeKind.KeywordChanged, "input.home"), (gap.Kind, gap.Path));
    }

    [Fact]
    public void ItemRefs_EscapedPointers_ArrayIndexes_AndBooleanTargets()
    {
        static string Schema(string zipType) =>
            $$$$"""{"type":"object","properties":{"tags":{"type":"array","items":{"$ref":"#/$defs/a~1b%20c"}},"first":{"$ref":"#/$defs/list/0"},"any":{"$ref":"#/$defs/yes"}},"$defs":{"a/b c":{"type":"object","properties":{"zip":{"type":"{{{{zipType}}}}"}}},"list":[{"type":"{{{{zipType}}}}"}],"yes":true}}""";

        var diff = Compare(Schema("number"), Schema("integer"));

        Assert.Equal(["input.first", "input.tags[].zip"], diff.Changes.Select(c => c.Path).Order(StringComparer.Ordinal));
        Assert.All(diff.Changes, c => Assert.True(c.IsBreaking));
        Assert.False(Compare(Schema("string"), Schema("string")).HasChanges);
    }

    [Fact]
    public void RequiredAndEnumInsideReferencedSchemas()
    {
        static string Schema(string required, string colors) =>
            $$$$"""{"type":"object","properties":{"car":{"$ref":"#/$defs/car"}},"$defs":{"car":{"type":"object","properties":{"color":{"enum":[{{{{colors}}}}]},"plate":{"type":"string"}},"required":[{{{{required}}}}]}}}""";

        var diff = Compare(Schema("\"color\"", "\"red\",\"blue\""), Schema("\"color\",\"plate\"", "\"red\""));

        Assert.Contains(diff.Changes, c => (c.Kind, c.Path, c.Severity) == (ToolSurfaceChangeKind.BecameRequired, "input.car.plate", ChangeSeverity.Breaking));
        Assert.Contains(diff.Changes, c => (c.Kind, c.Path, c.Severity) == (ToolSurfaceChangeKind.EnumChanged, "input.car.color", ChangeSeverity.Breaking));
        Assert.Equal(2, diff.Changes.Count);
    }

    [Fact]
    public void DefsThatFanOutALot_FinishQuickly()
    {
        // d0 -> two refs to d1 -> two refs to d2 ... : 2^40 paths if every ref were expanded without a limit.
        var defs = string.Join(",", Enumerable.Range(0, 40).Select(i =>
            $$$$"""
            "d{{{{i}}}}":{"type":"object","properties":{"l":{"$ref":"#/$defs/d{{{{i + 1}}}}"},"r":{"$ref":"#/$defs/d{{{{i + 1}}}}"}}}
            """));
        var schema = $$$$"""{"type":"object","properties":{"root":{"$ref":"#/$defs/d0"}},"$defs":{{{{{defs}}}},"d40":{"type":"string"}}}""";

        var changed = schema.Replace("""
            "d40":{"type":"string"}
            """, """
            "d40":{"type":"integer"}
            """, StringComparison.Ordinal);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(Compare(schema, schema).HasChanges);
        // Too many paths to expand them all, so the change falls back to an unclassified '$defs' keyword change instead of getting lost.
        var diff = Compare(schema, changed);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), watch.Elapsed.ToString());
        Assert.Contains(diff.Changes, c => c.Kind == ToolSurfaceChangeKind.KeywordChanged && c.Path == "input" && c.Message.Contains("'$defs'", StringComparison.Ordinal));
    }
}
