using McpProbe.Snapshots;
using static McpProbe.Tests.TestSupport.SchemaTools;

namespace McpProbe.Tests;

/// <summary>Schema shapes the comparer must survive: anything valid JSON Schema allows in a tool's inputSchema.</summary>
public sealed class ToolSurfaceComparerShapeTests
{
    private static ToolSurfaceDiff Compare(string before, string after) =>
        ToolSurfaceComparer.Compare(Surface(Tool("t", before)), Surface(Tool("t", after)));

    [Fact]
    public void NullableTypeArrays()
    {
        var nullable = """{"type":"object","properties":{"p":{"type":["string","null"]}}}""";
        var plain = """{"type":"object","properties":{"p":{"type":"string"}}}""";

        var narrowed = Compare(nullable, plain);
        Assert.True(narrowed.HasBreakingChanges, narrowed.ToReport());
        Assert.Contains("from null | string to string", narrowed.ToReport(), StringComparison.Ordinal);

        var widened = Compare(plain, nullable);
        Assert.False(widened.HasBreakingChanges, widened.ToReport());
        Assert.True(widened.HasChanges);
    }

    [Fact]
    public void BooleanSubschemas_DoNotThrow()
    {
        var anything = """{"type":"object","properties":{"p":true}}""";
        var typed = """{"type":"object","properties":{"p":{"type":"string"}}}""";

        Assert.False(Compare(anything, anything).HasChanges);
        Assert.True(Compare(anything, typed).HasBreakingChanges);
        Assert.False(Compare(typed, anything).HasBreakingChanges);
    }

    [Fact]
    public void TupleItems_DoNotThrow()
    {
        var tuple = """{"type":"object","properties":{"p":{"type":"array","items":[{"type":"string"},{"type":"integer"}]}}}""";
        var list = """{"type":"object","properties":{"p":{"type":"array","items":{"type":"string"}}}}""";

        Assert.False(Compare(tuple, tuple).HasChanges);
        Assert.True(Compare(tuple, list).HasChanges);
    }

    [Fact]
    public void RequiredWithoutDeclaredProperty()
    {
        var before = """{"type":"object","properties":{}}""";
        var after = """{"type":"object","properties":{},"required":["ghost"]}""";

        var diff = Compare(before, after);

        var change = Assert.Single(diff.Changes);
        Assert.Equal(ChangeSeverity.Breaking, change.Severity);
        Assert.Equal(ToolSurfaceChangeKind.BecameRequired, change.Kind);
        Assert.Equal("input.ghost", change.Path);
        Assert.False(Compare(after, before).HasBreakingChanges);
        Assert.False(Compare(after, after).HasChanges);
    }

    [Fact]
    public void RootWithoutProperties()
    {
        var empty = """{"type":"object"}""";
        var withProperty = """{"type":"object","properties":{"city":{"type":"string"}}}""";

        Assert.False(Compare(empty, empty).HasChanges);
        Assert.False(Compare(empty, withProperty).HasBreakingChanges);
        Assert.True(Compare(withProperty, empty).HasBreakingChanges);
    }

    [Fact]
    public void NonObjectValuesWhereObjectsAreExpected_DoNotThrow()
    {
        var odd = """{"type":"object","properties":{"p":{"type":"object","properties":5,"required":"p","enum":"x","items":"y"}}}""";
        var typed = """{"type":"object","properties":{"p":{"type":"object"}}}""";

        _ = Compare(odd, typed).ToReport();
        _ = Compare(typed, odd).ToReport();
        Assert.False(Compare(odd, odd).HasChanges);
    }

    [Fact]
    public void DeeplyNestedSchemas()
    {
        static string Nest(int depth, string leaf)
        {
            var json = leaf;
            for (var i = 0; i < depth; i++)
            {
                json = """{"type":"object","properties":{"n":""" + json + "}}";
            }

            return json;
        }

        var diff = Compare(Nest(30, """{"type":"string"}"""), Nest(30, """{"type":"integer"}"""));

        var change = Assert.Single(diff.Changes);
        Assert.Equal(ChangeSeverity.Breaking, change.Severity);
        Assert.StartsWith("input.n.n.n", change.Path, StringComparison.Ordinal);
    }
}
