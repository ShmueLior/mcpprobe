using McpProbe.Linting;
using McpProbe.Snapshots;
using McpProbe.Tests.TestSupport;
using ModelContextProtocol.Protocol;
using WeatherServer;
using static McpProbe.Tests.TestSupport.SchemaTools;

namespace McpProbe.Tests;

public sealed class ToolLinterTests
{
    private const string GoodDescription = "Does a clearly described, useful thing.";

    private static string Params(params (string Name, string? Description)[] parameters) =>
        "{\"type\":\"object\",\"properties\":{" +
        string.Join(",", parameters.Select(p => p.Description is null ? $"\"{p.Name}\":{{\"type\":\"string\"}}" : $"\"{p.Name}\":{{\"type\":\"string\",\"description\":\"{p.Description}\"}}")) +
        "}}";

    private static IReadOnlyList<LintFinding> Lint(LintOptions? options, params Tool[] tools) => ToolLinter.Lint(tools, options).Findings;

    private static IReadOnlyList<LintFinding> Lint(params Tool[] tools) => Lint(null, tools);

    [Fact]
    public async Task SampleServer_HasNoFindings()
    {
        await using var probe = await McpProbeHost.StartAsync(mcp => mcp.WithWeatherFeatures(), TestContext.Current.CancellationToken);

        var report = await probe.LintToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(report.Findings);
        Assert.False(report.Failed);
        Assert.Equal("No lint findings.", report.ToReport());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MCPP001_MissingDescription(string? description)
    {
        var finding = Assert.Single(Lint(Tool("search", description: description)));

        Assert.Equal(("MCPP001", LintSeverity.Error, "search", (string?)null), (finding.RuleId, finding.Severity, finding.ToolName, finding.ParameterName));
    }

    [Fact]
    public void MCPP002_ShortDescription_UsesConfigurableMinimum()
    {
        var tool = Tool("search", description: "Searches.");

        var finding = Assert.Single(Lint(tool));
        Assert.Equal("MCPP002", finding.RuleId);
        Assert.Contains("Description is 9 characters (minimum 20)", finding.Message, StringComparison.Ordinal);

        Assert.Empty(Lint(new LintOptions { MinToolDescriptionLength = 5 }, tool));
        Assert.Throws<ArgumentOutOfRangeException>(() => Lint(new LintOptions { MinToolDescriptionLength = -1 }, tool));
    }

    [Fact]
    public void MCPP003_ParameterWithoutDescription()
    {
        var finding = Assert.Single(Lint(Tool("search", Params(("query", null), ("limit", "Max results.")), GoodDescription)));

        Assert.Equal(("MCPP003", LintSeverity.Warning, "query"), (finding.RuleId, finding.Severity, finding.ParameterName));
        Assert.Equal("warning MCPP003 search.query: Parameter 'query' has no description. Say what it is and what format it expects.", finding.ToString());
    }

    [Theory]
    [InlineData("get weather")]
    [InlineData("get/weather")]
    [InlineData("wétter")]
    public void MCPP004_InvalidToolName(string name)
    {
        Assert.Contains(Lint(Tool(name, description: GoodDescription)), f => f.RuleId == "MCPP004");
    }

    [Fact]
    public void MCPP004_LengthLimits()
    {
        Assert.DoesNotContain(Lint(Tool(new string('a', 128), description: GoodDescription)), f => f.RuleId == "MCPP004");
        Assert.Contains(Lint(Tool(new string('a', 129), description: GoodDescription)), f => f.RuleId == "MCPP004");
        Assert.DoesNotContain(Lint(Tool("ns.get-weather_v2", description: GoodDescription)), f => f.RuleId == "MCPP004");
    }

    [Fact]
    public void MCPP005_FlagsToolsThatDeviateFromTheMajorityStyle()
    {
        var findings = Lint(
            Tool("get_weather", description: GoodDescription),
            Tool("get_alerts", description: GoodDescription),
            Tool("getRadar", description: GoodDescription),
            Tool("search", description: GoodDescription));

        var finding = Assert.Single(findings);
        Assert.Equal(("MCPP005", "getRadar"), (finding.RuleId, finding.ToolName));
        Assert.Equal("Tool name 'getRadar' is camelCase, but most tool names on this server are snake_case.", finding.Message);
    }

    [Fact]
    public void MCPP005_LowercaseWordsAndSingleStyleAreFine()
    {
        Assert.Empty(Lint(Tool("search", description: GoodDescription), Tool("fetch", description: GoodDescription), Tool("get_items", description: GoodDescription)));
        Assert.Empty(Lint(Tool("getItems", description: GoodDescription), Tool("search", description: GoodDescription)));
    }

    [Fact]
    public void MCPP006_ParameterNamingAcrossTools()
    {
        var findings = Lint(
            Tool("a_tool", Params(("city_name", "City."), ("max_days", "Days.")), GoodDescription),
            Tool("b_tool", Params(("zipCode", "Zip.")), GoodDescription));

        var finding = Assert.Single(findings);
        Assert.Equal(("MCPP006", "b_tool", "zipCode"), (finding.RuleId, finding.ToolName, finding.ParameterName));
    }

    [Fact]
    public void MCPP007_VagueParameterNames_AreInfoAndConfigurable()
    {
        var tool = Tool("store", Params(("data", "The thing to store.")), GoodDescription);

        var finding = Assert.Single(Lint(tool));
        Assert.Equal(("MCPP007", LintSeverity.Info), (finding.RuleId, finding.Severity));

        var options = new LintOptions();
        options.VagueParameterNames.Clear();
        options.VagueParameterNames.Add("stuff");
        Assert.Empty(Lint(options, tool));
        Assert.Single(Lint(options, Tool("store", Params(("Stuff", "Things.")), GoodDescription)));
    }

    [Fact]
    public void SeverityOverrides_ChangeOrDisableRules()
    {
        var tool = Tool("search", Params(("query", null)), "Searches.");
        var options = new LintOptions();
        options.Severities["mcpp003"] = LintSeverity.Error;
        options.Severities["MCPP002"] = LintSeverity.None;

        var finding = Assert.Single(Lint(options, tool));

        Assert.Equal(("MCPP003", LintSeverity.Error), (finding.RuleId, finding.Severity));
    }

    [Fact]
    public void Findings_AreOrderedBySeverityThenTool()
    {
        var findings = Lint(
            Tool("b_tool", Params(("data", "Data.")), GoodDescription),
            Tool("a_tool", Params(("q", null)), null));

        Assert.Equal(["MCPP001", "MCPP003", "MCPP007"], findings.Select(f => f.RuleId));
    }

    [Theory]
    [InlineData(LintSeverity.Error, false)]
    [InlineData(LintSeverity.Warning, true)]
    [InlineData(LintSeverity.Info, true)]
    [InlineData(LintSeverity.None, false)]
    public void FailOn_ControlsFailed(LintSeverity failOn, bool expected)
    {
        var report = ToolLinter.Lint([Tool("search", description: "Short.")], new LintOptions { FailOn = failOn });

        Assert.Equal(expected, report.Failed);
        if (expected)
        {
            var error = Assert.Throws<McpProbeAssertionException>(report.ThrowIfFailed);
            Assert.Contains("Lint failed", error.Message, StringComparison.Ordinal);
            Assert.Contains("MCPP002", error.Message, StringComparison.Ordinal);
        }
        else
        {
            report.ThrowIfFailed();
        }
    }

    [Fact]
    public void PassesLint_Assertion()
    {
        McpAssert.That([Tool("search", description: GoodDescription)]).PassesLint();
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That([Tool("search", description: null)]).PassesLint());
        var report = McpAssert.That([Tool("store", Params(("data", "The data.")), GoodDescription)]).PassesLint();
        Assert.Single(report.Findings);
    }

    [Fact]
    public void Report_SummarizesCounts()
    {
        var report = ToolLinter.Lint(Surface(Tool("a", description: null), Tool("b", Params(("x", null)), GoodDescription)));

        Assert.StartsWith("3 lint finding(s): 1 error, 1 warning, 1 info.", report.ToReport(), StringComparison.Ordinal);
    }

    [Fact]
    public void Rules_HaveUniqueIds()
    {
        Assert.Equal(LintRules.All.Count, LintRules.All.Select(r => r.Id).Distinct().Count());
        Assert.Equal(["MCPP001", "MCPP002", "MCPP003", "MCPP004", "MCPP005", "MCPP006", "MCPP007", "MCPP009", "MCPP010", "MCPP011", "MCPP012"], LintRules.All.Select(r => r.Id));
        Assert.DoesNotContain(LintRules.All, r => r.Id == "MCPP008");
    }

    [Theory]
    [InlineData("search", "Lower")]
    [InlineData("get_weather", "SnakeCase")]
    [InlineData("get_weather2", "SnakeCase")]
    [InlineData("get-weather", "KebabCase")]
    [InlineData("getWeather", "CamelCase")]
    [InlineData("GetWeather", "PascalCase")]
    [InlineData("Get_Weather", "Other")]
    [InlineData("get__weather", "Other")]
    [InlineData("_private", "Other")]
    [InlineData("v2", "Lower")]
    [InlineData("2fa", "Other")]
    public void NamingStyle_Detection(string name, string expected)
    {
        Assert.Equal(expected, NamingStyles.Detect(name).ToString());
    }
}
