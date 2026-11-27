using System.Diagnostics;
using McpProbe.Internal;
using McpProbe.Linting;
using McpProbe.Snapshots;
using static McpProbe.Tests.TestSupport.SchemaTools;

namespace McpProbe.Tests;

public sealed class PrintableTests
{
    [Theory]
    [InlineData("get_forecast", "get_forecast")]
    [InlineData("", "")]
    [InlineData("tab\there", "tab\\u0009here")]
    [InlineData("new\nline", "new\\u000Aline")]
    [InlineData("\u001b[31mred", "\\u001B[31mred")]
    [InlineData("bidi\u202Eevil", "bidi\\u202Eevil")]
    [InlineData("zero\u200Bwidth", "zero\\u200Bwidth")]
    [InlineData("back\\slash", "back\\\\slash")]
    [InlineData("wétter 天気", "wétter 天気")]
    [InlineData("emoji 😀", "emoji 😀")]
    public void Escapes(string input, string expected)
    {
        Assert.Equal(expected, Printable.Text(input));
    }

    [Fact]
    public void Backslash_CanBeKeptForFilePaths()
    {
        Assert.Equal(@"C:\repo\mcp-tools.json\u001B", Printable.Text("C:\\repo\\mcp-tools.json\u001b", escapeBackslash: false));
    }

    [Fact]
    public void Escapes_LoneSurrogate()
    {
        // Built in code: xUnit's argument serialization would replace a lone surrogate in InlineData.
        Assert.Equal("lone \\uD83D surrogate", Printable.Text("lone " + (char)0xD83D + " surrogate"));
        Assert.Equal("end \\uDE00", Printable.Text("end " + (char)0xDE00));
    }

    [Fact]
    public void Truncates_WithoutSplittingAnEscapeOrSurrogatePair()
    {
        Assert.Equal("abcdefg...", Printable.Text("abcdefghijk", 10));
        Assert.Equal("abcdefghij", Printable.Text("abcdefghij", 10));

        // An escape is 6 characters and never cut in half.
        Assert.Equal("ab...", Printable.Text("ab\u001bcdefgh", 8));
        Assert.Equal("ab\\u001B", Printable.Text("ab\u001b", 8));

        // A surrogate pair is never cut in half.
        var cut = Printable.Text("abcde😀fgh", 9);
        Assert.Equal("abcde...", cut);
        Assert.Equal("abcd😀...", Printable.Text("abcd😀fghij", 9));
    }

    [Fact]
    public void HugeInput_IsFastAndBounded()
    {
        var huge = new string('\u001b', 5_000_000);
        var stopwatch = Stopwatch.StartNew();

        var result = Printable.Text(huge);

        Assert.True(result.Length <= Printable.DefaultMaxLength, $"length {result.Length}");
        Assert.EndsWith("...", result, StringComparison.Ordinal);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public void Reports_EscapeServerSuppliedNames()
    {
        var diff = ToolSurfaceComparer.Compare(Surface(Tool("evil\u001b[2Jtool")), Surface());
        Assert.Contains("evil\\u001B[2Jtool: Tool was removed.", diff.ToReport(), StringComparison.Ordinal);
        Assert.DoesNotContain('\u001b', diff.ToReport());

        var report = ToolLinter.Lint(Surface(Tool("t\u202Eabc")));
        Assert.DoesNotContain('\u202E', report.ToReport());
        Assert.Contains("t\\u202Eabc", report.ToReport(), StringComparison.Ordinal);
    }

    [Fact]
    public void AssertionMessages_CapLongToolLists()
    {
        var tools = Enumerable.Range(0, 500).Select(i => Tool($"tool_{i:D3}")).ToList();

        var error = Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(tools).ContainsTool("missing"));

        Assert.Contains("'tool_019' and 480 more", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tool_020", error.Message, StringComparison.Ordinal);
    }
}
