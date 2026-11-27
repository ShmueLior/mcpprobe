using System.Text.Json.Nodes;
using McpProbe.Linting;
using McpProbe.Snapshots;

namespace McpProbe.Tests;

/// <summary>Contract-quality rules MCPP013-MCPP016. They are on by default and use <see cref="LintOptions.FailOn"/>.</summary>
public sealed class ToolLinterQualityTests
{
    private const string GoodDescription = "Does a clearly described, useful thing.";

    private static ToolSurface Surface(
        string name,
        string? description = GoodDescription,
        string inputSchema = """{"type":"object"}""",
        string? outputSchema = null,
        string? annotations = null,
        string? title = null)
    {
        var tool = new JsonObject
        {
            ["name"] = name,
            ["inputSchema"] = JsonNode.Parse(inputSchema),
        };
        if (description is not null)
        {
            tool["description"] = description;
        }

        if (title is not null)
        {
            tool["title"] = title;
        }

        if (outputSchema is not null)
        {
            tool["outputSchema"] = JsonNode.Parse(outputSchema);
        }

        if (annotations is not null)
        {
            tool["annotations"] = JsonNode.Parse(annotations);
        }

        return ToolSurface.Parse(new JsonObject { ["formatVersion"] = 1, ["tools"] = new JsonArray(tool) }.ToJsonString());
    }

    private static IReadOnlyList<LintFinding> Lint(ToolSurface surface, LintOptions? options = null) =>
        ToolLinter.Lint(surface, options).Findings;

    private static IEnumerable<LintFinding> Rule(ToolSurface surface, string ruleId, LintOptions? options = null) =>
        Lint(surface, options).Where(f => f.RuleId == ruleId);

    private static string Prop(string schema) => "{\"type\":\"object\",\"properties\":{\"p\":" + schema + "}}";

    // MCPP013

    [Fact]
    public void MCPP013_ReadOnlyAndDestructive_IsAFinding()
    {
        var finding = Assert.Single(Lint(Surface("get_file", annotations: """{"readOnlyHint":true,"destructiveHint":true}""")));

        Assert.Equal(("MCPP013", LintSeverity.Warning, (string?)null), (finding.RuleId, finding.Severity, finding.ParameterName));
        Assert.Contains("read-only and destructive", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("get_file", """{"readOnlyHint":true}""")]
    [InlineData("get_file", """{"destructiveHint":false}""")]
    [InlineData("delete_file", null)]
    [InlineData("delete_file", """{"destructiveHint":true}""")]
    [InlineData("delete_file", """{"idempotentHint":true}""")]
    [InlineData("delete_file", """{"readOnlyHint":false}""")]
    [InlineData("undelete_file", """{"destructiveHint":false}""")]
    [InlineData("dropdown_options", """{"readOnlyHint":true}""")]
    [InlineData("clearance_level", """{"readOnlyHint":true}""")]
    public void MCPP013_StaysQuiet(string name, string? annotations)
    {
        Assert.Empty(Rule(Surface(name, annotations: annotations), "MCPP013"));
    }

    [Theory]
    [InlineData("delete_file", "delete")]
    [InlineData("remove-item", "remove")]
    [InlineData("drop.table", "drop")]
    [InlineData("PurgeCache", "purge")]
    [InlineData("deleteFile", "delete")]
    [InlineData("files_WIPE", "wipe")]
    [InlineData("eraseAll", "erase")]
    public void MCPP013_DestructiveNameWithDestructiveHintFalse(string name, string token)
    {
        var finding = Assert.Single(Rule(Surface(name, annotations: """{"destructiveHint":false}"""), "MCPP013"));

        Assert.Contains($"contains '{token}'", finding.Message, StringComparison.Ordinal);
        Assert.Contains("destructiveHint is false", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MCPP013_DestructiveNameMarkedReadOnly()
    {
        var finding = Assert.Single(Rule(Surface("delete_file", annotations: """{"readOnlyHint":true}"""), "MCPP013"));
        Assert.Contains("readOnlyHint is true", finding.Message, StringComparison.Ordinal);

        // Both explicit: one finding, the destructiveHint one.
        var both = Assert.Single(Rule(Surface("delete_file", annotations: """{"readOnlyHint":true,"destructiveHint":false}"""), "MCPP013"));
        Assert.Contains("destructiveHint is false", both.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MCPP013_TokensAreConfigurable_AndRuleCanBeTurnedOff()
    {
        var archive = Surface("archive_post", annotations: """{"destructiveHint":false}""");
        Assert.Empty(Rule(archive, "MCPP013"));

        var options = new LintOptions();
        options.DestructiveNameTokens.Add("ARCHIVE");
        Assert.Single(Rule(archive, "MCPP013", options));

        var off = new LintOptions();
        off.Severities["MCPP013"] = LintSeverity.None;
        Assert.Empty(Rule(Surface("x", annotations: """{"readOnlyHint":true,"destructiveHint":true}"""), "MCPP013", off));
    }

    // MCPP014

    [Theory]
    [InlineData("Searches orders. Returns JSON with one entry per order.")]
    [InlineData("Searches orders and returns structured content for each match.")]
    [InlineData("Searches orders. Returns an object with the totals.")]
    [InlineData("Searches orders; see the OUTPUT   SCHEMA in the docs.")]
    public void MCPP014_CueWithoutOutputSchema_IsAFinding(string description)
    {
        var finding = Assert.Single(Lint(Surface("search_orders", description)));

        Assert.Equal(("MCPP014", LintSeverity.Warning, (string?)null), (finding.RuleId, finding.Severity, finding.ParameterName));
        Assert.Contains("no outputSchema", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Searches orders. Returns a list of matching orders, one per line.")]
    [InlineData("Searches orders. Returns a string.")]
    [InlineData("Accepts a JSON filter and searches orders.")]
    [InlineData("Searches orders. Returns JSONL, one order per line.")]
    [InlineData("Searches orders and returns jsonish text.")]
    public void MCPP014_StaysQuietWithoutAClearCue(string description)
    {
        Assert.Empty(Rule(Surface("search_orders", description), "MCPP014"));
    }

    [Fact]
    public void MCPP014_StaysQuietWhenOutputSchemaIsDeclared_AndCuesAreConfigurable()
    {
        Assert.Empty(Rule(Surface("search_orders", "Searches orders. Returns JSON.", outputSchema: """{"type":"object"}"""), "MCPP014"));

        var options = new LintOptions();
        options.StructuredOutputCues.Clear();
        options.StructuredOutputCues.Add("Returns  YAML");
        Assert.Empty(Rule(Surface("search_orders", "Searches orders. Returns JSON."), "MCPP014", options));
        Assert.Single(Rule(Surface("search_orders", "Searches orders. returns yaml."), "MCPP014", options));
    }

    // MCPP015

    [Theory]
    [InlineData("get_forecast", "get_forecast")]
    [InlineData("get_forecast", "Get forecast.")]
    [InlineData("get_forecast", "  get   FORECAST:  ")]
    [InlineData("GetForecast", "Get forecast")]
    [InlineData("getHTTPStatus", "get http status")]
    [InlineData("files.list-all", "Files list all.")]
    public void MCPP015_DescriptionRestatesName(string name, string description)
    {
        var finding = Assert.Single(Lint(Surface(name, description)));

        Assert.Equal(("MCPP015", LintSeverity.Warning, (string?)null), (finding.RuleId, finding.Severity, finding.ParameterName));
    }

    [Fact]
    public void MCPP015_DescriptionRestatesTitle()
    {
        Assert.Single(Rule(Surface("forecast_v2", "Daily Forecast.", title: "Daily forecast"), "MCPP015"));
        Assert.Empty(Rule(Surface("forecast_v2", "Gets a daily weather forecast for a city.", title: "Daily forecast"), "MCPP015"));
    }

    [Fact]
    public void MCPP015_RealDescription_AndMissingDescription()
    {
        Assert.Empty(Lint(Surface("get_forecast", "Gets a daily weather forecast for a city. Use it for upcoming weather.")));
        Assert.Equal("MCPP001", Assert.Single(Lint(Surface("get_forecast", "  "))).RuleId);
    }

    [Fact]
    public void MCPP015_ReplacesMCPP002_UnlessTurnedOff()
    {
        var shortTautology = Surface("search", "Search.");
        Assert.Equal("MCPP015", Assert.Single(Lint(shortTautology)).RuleId);

        var off = new LintOptions();
        off.Severities["MCPP015"] = LintSeverity.None;
        Assert.Equal("MCPP002", Assert.Single(Lint(shortTautology, off)).RuleId);
    }

    // MCPP016

    [Theory]
    [InlineData("""{"type":"string","enum":[]}""", "inputSchema.properties.p")]
    [InlineData("""{"type":"array","items":{"type":"string","enum":[]}}""", "inputSchema.properties.p.items")]
    [InlineData("""{"$ref":"#/$defs/unit","description":"Unit."}""", "inputSchema.$defs.unit")]
    public void MCPP016_EmptyEnum(string property, string path)
    {
        var schema = "{\"type\":\"object\",\"properties\":{\"p\":" + property + "},\"$defs\":{\"unit\":{\"type\":\"string\",\"enum\":[]}}}";
        if (!property.Contains("$ref", StringComparison.Ordinal))
        {
            schema = Prop(property);
        }

        var finding = Assert.Single(Rule(Surface("t", inputSchema: schema), "MCPP016"));
        Assert.Equal((LintSeverity.Warning, path), (finding.Severity, finding.ParameterName));
        Assert.Contains("Empty enum accepts no value", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MCPP016_ChecksOutputSchema()
    {
        var finding = Assert.Single(Rule(Surface("t", outputSchema: """{"type":"object","properties":{"state":{"enum":["ok","failed"]}}}"""), "MCPP016"));
        Assert.Equal("outputSchema.properties.state", finding.ParameterName);
    }

    [Theory]
    [InlineData("""{"enum":["a","b"]}""", "Add \"type\": \"string\" next to it")]
    [InlineData("""{"enum":[1,2]}""", "Add \"type\": \"integer\" next to it")]
    [InlineData("""{"enum":[1,2.5]}""", "Add \"type\": \"number\" next to it")]
    [InlineData("""{"enum":[1.0]}""", "Add \"type\": \"integer\" next to it")]
    [InlineData("""{"enum":[true]}""", "Add \"type\": \"boolean\" next to it")]
    [InlineData("""{"enum":["a",null]}""", "Add \"type\": [\"string\", \"null\"] next to it")]
    public void MCPP016_EnumWithoutType_SuggestsTheType(string property, string hint)
    {
        var finding = Assert.Single(Rule(Surface("t", inputSchema: Prop(property)), "MCPP016"));
        Assert.Equal("inputSchema.properties.p", finding.ParameterName);
        Assert.Contains(hint, finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"type":"string","enum":["a"]}""")]
    [InlineData("""{"type":["string","null"],"enum":["a",null]}""")]
    [InlineData("""{"enum":["a",1]}""")]
    [InlineData("""{"enum":[null]}""")]
    [InlineData("""{"enum":"a"}""")]
    [InlineData("""{"type":"string"}""")]
    [InlineData("""{"type":"string","anyOf":[{"enum":["a","b"]},{"pattern":"^x"}]}""")]
    [InlineData("""{"type":"string","not":{"enum":[]}}""")]
    [InlineData("""{"type":"string","not":{"enum":["a"]}}""")]
    public void MCPP016_StaysQuiet(string property)
    {
        Assert.Empty(Rule(Surface("t", inputSchema: Prop(property)), "MCPP016"));
    }

    [Fact]
    public void MCPP016_EmptyEnumInsideACombinatorIsStillAFinding()
    {
        var finding = Assert.Single(Rule(Surface("t", inputSchema: Prop("""{"anyOf":[{"type":"string","enum":[]},{"type":"integer"}]}""")), "MCPP016"));
        Assert.Equal("inputSchema.properties.p.anyOf[0]", finding.ParameterName);
    }

    [Fact]
    public void MCPP016_RunsWithoutNestedOrStrict_AndHandlesRecursiveRefs()
    {
        var schema = """
            {"type":"object","properties":{"node":{"$ref":"#/$defs/node"}},
             "$defs":{"node":{"type":"object","properties":{"kind":{"enum":["leaf","branch"]},"children":{"type":"array","items":{"$ref":"#/$defs/node"}}}}}}
            """;
        var options = new LintOptions { CheckNestedParameters = false, Strict = false };

        var finding = Assert.Single(Rule(Surface("t", inputSchema: schema), "MCPP016", options));
        Assert.Equal("inputSchema.$defs.node.properties.kind", finding.ParameterName);

        options.Severities["MCPP016"] = LintSeverity.None;
        Assert.Empty(Rule(Surface("t", inputSchema: schema), "MCPP016", options));
    }

    [Fact]
    public void MCPP016_FindsDeeplyNestedEnums()
    {
        var schema = string.Concat(Enumerable.Repeat("{\"type\":\"array\",\"items\":", 50)) + "{\"enum\":[]}" + new string('}', 50);
        var finding = Assert.Single(Rule(Surface("t", inputSchema: Prop(schema)), "MCPP016"));
        Assert.EndsWith(".items", finding.ParameterName, StringComparison.Ordinal);
    }

    // Shared behavior

    [Fact]
    public void QualityWarnings_UseFailOn()
    {
        var surface = Surface("search", "Search.");

        Assert.True(ToolLinter.Lint(surface).Failed);
        Assert.False(ToolLinter.Lint(surface, new LintOptions { FailOn = LintSeverity.Error }).Failed);

        var upgraded = new LintOptions { FailOn = LintSeverity.Error };
        upgraded.Severities["MCPP015"] = LintSeverity.Error;
        Assert.True(ToolLinter.Lint(surface, upgraded).Failed);
    }

    [Fact]
    public void Messages_StayUnderTheReportCap_WithLongNames()
    {
        var name = "delete_" + new string('x', 121);
        var findings = Lint(Surface(name, "Returns JSON. " + name, inputSchema: Prop("""{"enum":["a",null]}"""), annotations: """{"destructiveHint":false}"""))
            .Concat(Lint(Surface(name, name)))
            .Concat(Lint(Surface(name, inputSchema: Prop("""{"type":"string","enum":[]}"""), annotations: """{"readOnlyHint":true,"destructiveHint":true}""")))
            .Concat(Lint(Surface(name, inputSchema: "{}", annotations: """{"readOnlyHint":true}""")))
            .ToList();

        foreach (var id in new[] { "MCPP013", "MCPP014", "MCPP015", "MCPP016" })
        {
            Assert.Contains(findings, f => f.RuleId == id);
        }

        Assert.All(findings, f =>
        {
            Assert.True(f.Message.Length < 500, $"{f.RuleId} message is {f.Message.Length} characters.");
            Assert.EndsWith(f.Message, f.ToString(), StringComparison.Ordinal);
        });
    }
}
