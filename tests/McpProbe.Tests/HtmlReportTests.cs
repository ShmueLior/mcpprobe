using System.Globalization;
using McpProbe.Linting;
using McpProbe.Snapshots;
using static McpProbe.Tests.TestSupport.SchemaTools;

namespace McpProbe.Tests;

public sealed class HtmlReportTests
{
    private const string Hostile = "<script>alert(1)</script> & \"q\" 'a' \u001b[31m \u202E";

    private static LintFinding Finding(string rule, LintSeverity severity, string tool = "tool", string? parameter = null, string message = "Fix it.") =>
        new(rule, severity, tool, parameter, message);

    private static ToolSurfaceChange Change(ChangeSeverity severity, ToolSurfaceChangeKind kind = ToolSurfaceChangeKind.ToolRemoved, string tool = "tool", string? path = null, string message = "Changed.") =>
        new(tool, path, kind, severity, message);

    [Fact]
    public void Lint_NoFindings_IsASmallPassedPage()
    {
        var html = new LintReport([], LintSeverity.Warning).ToHtml();

        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.Contains("<meta charset=\"utf-8\">", html, StringComparison.Ordinal);
        Assert.Contains("McpProbe lint report", html, StringComparison.Ordinal);
        Assert.Contains("Passed", html, StringComparison.Ordinal);
        Assert.Contains("No lint findings.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<table", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Lint_ShowsEveryColumnAndWhetherAFindingFailsTheRun()
    {
        var report = new LintReport(
            [Finding("MCPP003", LintSeverity.Error, "search", "home.zip", "No description."), Finding("MCPP014", LintSeverity.Warning, "other")],
            LintSeverity.Warning);
        var html = report.ToHtml();

        Assert.Contains("<th>Fails the run</th>", html, StringComparison.Ordinal);
        Assert.Contains("<tr class=\"error\"><td>error</td><td>MCPP003</td><td>search</td><td>home.zip</td><td>No description.</td><td>yes</td></tr>", html, StringComparison.Ordinal);
        Assert.Contains("<tr class=\"warning\"><td>warning</td><td>MCPP014</td><td>other</td><td></td><td>Fix it.</td><td>yes</td></tr>", html, StringComparison.Ordinal);
        Assert.Contains("Failed", html, StringComparison.Ordinal);
        Assert.Contains("2 lint finding(s): 1 error, 1 warning, 0 info. Fails on: warning.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Lint_FailOnError_WarningsPassAndSayTheyDoNotFail()
    {
        var html = new LintReport([Finding("MCPP002", LintSeverity.Warning)], LintSeverity.Error).ToHtml();

        Assert.Contains("class=\"banner pass\">Passed", html, StringComparison.Ordinal);
        Assert.Contains("<td>no</td>", html, StringComparison.Ordinal);
        Assert.Contains("Fails on: error.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Lint_PortabilityWarningDoesNotFail_ButPortabilityErrorDoes()
    {
        var warning = new LintReport([Finding("MCPP010", LintSeverity.Warning, parameter: "inputSchema.properties.city")], LintSeverity.Info).ToHtml();
        var error = new LintReport([Finding("MCPP009", LintSeverity.Error, parameter: "inputSchema.properties.city")], LintSeverity.None).ToHtml();

        Assert.Contains("banner pass", warning, StringComparison.Ordinal);
        Assert.Contains("<td>inputSchema.properties.city</td><td>Fix it.</td><td>no</td>", warning, StringComparison.Ordinal);
        Assert.Contains("banner fail", error, StringComparison.Ordinal);
        Assert.Contains("<td>yes</td>", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Lint_FailOnNone_NothingFailsExceptPortabilityErrors()
    {
        var html = new LintReport([Finding("MCPP013", LintSeverity.Error)], LintSeverity.None).ToHtml();

        Assert.Contains("banner pass", html, StringComparison.Ordinal);
        Assert.Contains("Fails on: none.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Lint_HostileServerText_IsOnlyEverElementText()
    {
        var html = new LintReport([Finding("MCPP001", LintSeverity.Error, Hostile, Hostile, Hostile)], LintSeverity.Warning).ToHtml();

        AssertEncoded(html);
        Assert.Equal(3, CountOf(html, "&lt;script&gt;alert(1)&lt;/script&gt;"));
        Assert.Equal(1, CountOf(html, "<table"));
    }

    [Fact]
    public void Diff_HostileServerText_IsOnlyEverElementText()
    {
        var html = new ToolSurfaceDiff([Change(ChangeSeverity.Breaking, tool: Hostile, path: Hostile, message: Hostile)]).ToHtml();

        AssertEncoded(html);
        Assert.Equal(3, CountOf(html, "&lt;script&gt;alert(1)&lt;/script&gt;"));
    }

    [Fact]
    public void LongText_IsCappedLikeTheTextReports()
    {
        var longText = new string('a', 600);
        var lint = new LintReport([Finding("MCPP001", LintSeverity.Error, longText, longText, longText)], LintSeverity.Warning).ToHtml();
        var diff = new ToolSurfaceDiff([Change(ChangeSeverity.Breaking, tool: longText, path: longText, message: longText)]).ToHtml();

        Assert.Contains($"<td>{new string('a', 497)}...</td>", lint, StringComparison.Ordinal);
        Assert.Contains($"<td>{new string('a', 97)}...</td>", lint, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('a', 498), lint, StringComparison.Ordinal);
        Assert.Contains($"<td>{new string('a', 197)}...</td>", diff, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('a', 501), diff, StringComparison.Ordinal);
    }

    [Fact]
    public void LoneSurrogatesAndBackslashes_FollowToString()
    {
        var text = "ab\ud83d" + "cd\\e";
        var html = new LintReport([Finding("MCPP001", LintSeverity.Error, text)], LintSeverity.Warning).ToHtml();

        Assert.DoesNotContain(html, char.IsSurrogate);
        Assert.Contains("<td>ab\\uD83Dcd\\\\e</td>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Diff_Banners_FollowBreakingAndNonBreaking()
    {
        var unchanged = new ToolSurfaceDiff([]).ToHtml();
        var nonBreaking = new ToolSurfaceDiff([Change(ChangeSeverity.NonBreaking, ToolSurfaceChangeKind.ToolAdded, path: "input.city")]).ToHtml();
        var mixed = new ToolSurfaceDiff([Change(ChangeSeverity.Breaking), Change(ChangeSeverity.NonBreaking, ToolSurfaceChangeKind.PropertyAdded)]).ToHtml();

        Assert.Contains("class=\"banner pass\">Unchanged", unchanged, StringComparison.Ordinal);
        Assert.Contains("Tool surface unchanged.", unchanged, StringComparison.Ordinal);
        Assert.DoesNotContain("<table", unchanged, StringComparison.Ordinal);
        Assert.Contains("class=\"banner note\">Non-breaking changes only", nonBreaking, StringComparison.Ordinal);
        Assert.Contains("<tr class=\"non-breaking\"><td>non-breaking</td><td>ToolAdded</td><td>tool</td><td>input.city</td><td>Changed.</td></tr>", nonBreaking, StringComparison.Ordinal);
        Assert.Contains("class=\"banner fail\">Breaking changes", mixed, StringComparison.Ordinal);
        Assert.Contains("Tool surface changed: 1 breaking, 1 non-breaking.", mixed, StringComparison.Ordinal);
        Assert.Contains("<tr class=\"breaking\"><td>breaking</td><td>ToolRemoved</td>", mixed, StringComparison.Ordinal);
    }

    [Fact]
    public void Diff_RealComparison_ListsConstraintAndKeywordChanges()
    {
        var before = Tool("t", """{"type":"object","properties":{"n":{"type":"integer","maximum":10}}}""");
        var after = Tool("t", """{"type":"object","properties":{"n":{"type":"integer","maximum":5}}}""");

        var html = Diff(before, after).ToHtml();

        Assert.Contains("<td>ConstraintChanged</td>", html, StringComparison.Ordinal);
        Assert.Contains("input.n", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    public void Counts_UseInvariantCulture(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var findings = Enumerable.Range(0, 1234).Select(_ => Finding("MCPP001", LintSeverity.Error)).ToList();
            var html = new LintReport(findings, LintSeverity.Warning).ToHtml();

            Assert.Contains("1234 lint finding(s): 1234 error, 0 warning, 0 info.", html, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static void AssertEncoded(string html)
    {
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\u001b', html);
        Assert.DoesNotContain('\u202E', html);
        Assert.Contains("\\u001B[31m", html, StringComparison.Ordinal);
        Assert.Contains("\\u202E", html, StringComparison.Ordinal);
        Assert.Contains("&amp; &quot;q&quot; &#39;a&#39;", html, StringComparison.Ordinal);
    }

    private static int CountOf(string text, string value) => text.Split(value).Length - 1;
}
