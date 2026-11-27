using System.Text.Json.Nodes;
using McpProbe.Linting;
using McpProbe.Snapshots;
using WeatherServer;
using static McpProbe.Tests.TestSupport.SchemaTools;

namespace McpProbe.Tests;

/// <summary>
/// Schema-portability rules MCPP009-MCPP012, enabled by <see cref="LintOptions.Strict"/>
/// or a per-rule severity override. Default lint must stay unchanged when strict is off.
/// </summary>
public sealed class ToolLinterStrictTests
{
    private const string GoodDescription = "Does a clearly described, useful thing.";

    private static readonly string UnportableCity = """
        {"type":"object","properties":{"city":{"description":"City name.","anyOf":[true,{"type":["null","boolean"]},{},{"$ref":"https://example.com/schema.json"}]}}}
        """;

    private static LintOptions Strict(Action<LintOptions>? configure = null)
    {
        var options = new LintOptions { Strict = true };
        // Boolean and empty subschemas cannot carry a description, so keep MCPP003/007 out of these cases.
        options.Severities[LintRules.ParameterMissingDescription.Id] = LintSeverity.None;
        options.Severities[LintRules.VagueParameterName.Id] = LintSeverity.None;
        configure?.Invoke(options);
        return options;
    }

    // Tool.InputSchema rejects many legal JSON Schema roots (the SDK requires a tool input object),
    // so these tests go through the snapshot parser, the same path as 'mcpprobe lint --from-file'.
    private static IReadOnlyList<LintFinding> Lint(string inputSchema, LintOptions? options = null, string? outputSchema = null) =>
        ToolLinter.Lint(ParseSurface("t", inputSchema, GoodDescription, outputSchema), options ?? Strict()).Findings;

    private static ToolSurface ParseSurface(string name, string inputSchema, string description, string? outputSchema = null)
    {
        var tool = new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = JsonNode.Parse(inputSchema),
        };
        if (outputSchema is not null)
        {
            tool["outputSchema"] = JsonNode.Parse(outputSchema);
        }

        var json = new JsonObject
        {
            ["formatVersion"] = 1,
            ["tools"] = new JsonArray(tool),
        };
        return ToolSurface.Parse(json.ToJsonString());
    }

    private static LintFinding Single(IEnumerable<LintFinding> findings, string ruleId) =>
        Assert.Single(findings, f => f.RuleId == ruleId);

    [Fact]
    public void Strict_DefaultsToOff_AndProducesNoPortabilityFindings()
    {
        Assert.False(new LintOptions().Strict);
        var findings = Lint(UnportableCity, new LintOptions());
        Assert.DoesNotContain(findings, f => f.RuleId is "MCPP009" or "MCPP010" or "MCPP011" or "MCPP012");
    }

    [Fact]
    public async Task SampleServer_HasNoFindings_WithStrict()
    {
        await using var probe = await McpProbeHost.StartAsync(mcp => mcp.WithWeatherFeatures(), TestContext.Current.CancellationToken);

        var report = await probe.LintToolsAsync(new LintOptions { Strict = true }, TestContext.Current.CancellationToken);

        Assert.Empty(report.Findings);
        Assert.False(report.Failed);
    }

    [Theory]
    [InlineData("""{"type":"object","properties":{"city":true}}""", "inputSchema.properties.city", "true")]
    [InlineData("""{"type":"object","properties":{"city":false}}""", "inputSchema.properties.city", "false")]
    [InlineData("""{"type":"object","properties":{"city":{"description":"City.","items":true}}}""", "inputSchema.properties.city.items", "true")]
    [InlineData("""{"type":"object","properties":{"city":{"description":"City.","anyOf":[true]}}}""", "inputSchema.properties.city.anyOf[0]", "true")]
    [InlineData("""{"type":"object","properties":{"city":{"description":"City.","not":false}}}""", "inputSchema.properties.city.not", "false")]
    public void MCPP009_FlagsBareBooleanSchemas(string schema, string path, string literal)
    {
        var finding = Single(Lint(schema), "MCPP009");
        Assert.Equal((LintSeverity.Error, "t", path), (finding.Severity, finding.ToolName, finding.ParameterName));
        Assert.Contains($"Bare '{literal}' used where a schema object is expected", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("additionalProperties")]
    [InlineData("unevaluatedProperties")]
    [InlineData("additionalItems")]
    [InlineData("unevaluatedItems")]
    public void MCPP009_DoesNotFireOnBooleanValuedKeywords(string keyword)
    {
        foreach (var value in new[] { "true", "false" })
        {
            var schema = """{"type":"object","properties":{"city":{"type":"string","description":"City.","KW":VAL}}}""".Replace("KW", keyword, StringComparison.Ordinal).Replace("VAL", value, StringComparison.Ordinal);
            Assert.DoesNotContain(Lint(schema), f => f.RuleId == "MCPP009");
        }
    }

    [Fact]
    public void MCPP009_FiresOnOutputSchema_AndSuggestsDifferentFixUnderNot()
    {
        var output = Lint("""{"type":"object"}""", outputSchema: """{"type":"object","properties":{"data":true}}""");
        var finding = Single(output, "MCPP009");
        Assert.Equal("outputSchema.properties.data", finding.ParameterName);
        Assert.Contains("Declare what the value actually is", finding.Message, StringComparison.Ordinal);

        var underNot = Single(Lint("""{"type":"object","properties":{"city":{"description":"City.","not":true}}}"""), "MCPP009");
        Assert.Equal("inputSchema.properties.city.not", underNot.ParameterName);
        Assert.Contains("Under `not`, use {}", underNot.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Declare what the value actually is", underNot.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""["null","boolean"]""", true)]
    [InlineData("""["string","number","integer"]""", false)]
    [InlineData("""["string","null"]""", true)]
    [InlineData("""["null","boolean","object","array","number","string","integer"]""", true)]
    public void MCPP010_FlagsLegalTypeUnions(string type, bool nullCaveat)
    {
        var finding = Single(Lint($$"""{"type":{{type}}}"""), "MCPP010");
        Assert.Equal((LintSeverity.Warning, "inputSchema"), (finding.Severity, finding.ParameterName));
        Assert.Contains("`type` is an array", finding.Message, StringComparison.Ordinal);
        Assert.Contains("anyOf", finding.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("optional", finding.Message.Split("anyOf")[0], StringComparison.Ordinal);
        Assert.Equal(nullCaveat, finding.Message.Contains("nullable: true", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""["string","null"]""", true)]
    [InlineData("""["null","boolean"]""", true)]
    [InlineData("""["string","number","integer"]""", false)]
    [InlineData("""["null","boolean","object","array","number","string","integer"]""", true)]
    public void MCPP010_PrintedReportKeepsTheFullMessage(string type, bool nullCaveat)
    {
        var surface = ParseSurface("t", $$"""{"type":{{type}}}""", GoodDescription);
        var report = ToolLinter.Lint(surface, Strict());
        var finding = Single(report.Findings, "MCPP010");
        Assert.InRange(finding.Message.Length, 1, 500);
        var printed = finding.ToString();
        var reportText = report.ToReport();
        Assert.DoesNotContain("...", printed, StringComparison.Ordinal);
        Assert.Contains(finding.Message, printed, StringComparison.Ordinal);
        Assert.Contains(finding.Message, reportText, StringComparison.Ordinal);
        Assert.Equal(nullCaveat, printed.Contains("nullable: true", StringComparison.Ordinal));
        Assert.Equal(nullCaveat, reportText.Contains("nullable: true", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{"type":"string"}""")]
    [InlineData("""{"type":[]}""")]
    [InlineData("""{"type":["string","string"]}""")]
    [InlineData("""{"type":["bananas"]}""")]
    [InlineData("""{"type":[3]}""")]
    public void MCPP010_StaysQuietOnSingleTypeAndMalformedArrays(string schema)
    {
        Assert.DoesNotContain(Lint(schema), f => f.RuleId == "MCPP010");
    }

    [Theory]
    [InlineData("""{"type":"object","properties":{"city":{"description":"City."}}}""")]
    [InlineData("""{"type":"object","properties":{"city":{"description":"City.","title":"City","$defs":{"x":{"type":"string"}}}}}""")]
    public void MCPP011_FlagsEmptyAndAnnotationOnlySchemas(string schema)
    {
        var finding = Single(Lint(schema), "MCPP011");
        Assert.Equal("inputSchema.properties.city", finding.ParameterName);
        Assert.Contains("no validation keyword", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"type":"string"}""")]
    [InlineData("""{"enum":["a"]}""")]
    [InlineData("""{"properties":{"x":{"type":"string"}}}""")]
    [InlineData("""{"$ref":"#/$defs/x","$defs":{"x":{"type":"string"}}}""")]
    [InlineData("""{"format":"email"}""")]
    [InlineData("""{"if":{"const":1},"then":{"type":"string"}}""")]
    public void MCPP011_StaysQuietWhenAConstrainingKeywordIsPresent(string schema)
    {
        Assert.DoesNotContain(Lint(schema), f => f.RuleId == "MCPP011" && f.ParameterName == "inputSchema");
    }

    [Fact]
    public void MCPP011_IfAloneIsAFinding_AndSchemaUnderNotIsExempt()
    {
        Assert.Equal("inputSchema", Single(Lint("""{"if":{"const":1}}"""), "MCPP011").ParameterName);
        Assert.DoesNotContain(Lint("""{"not":{}}"""), f => f.RuleId == "MCPP011");
        Assert.DoesNotContain(Lint("""{"not":{"description":"nope"}}"""), f => f.RuleId == "MCPP011");
        Assert.Contains(Lint("""{"not":{"anyOf":[{}]}}"""), f => f.RuleId == "MCPP011" && f.ParameterName == "inputSchema.not.anyOf[0]");
    }

    [Theory]
    [InlineData("""{"$ref":"https://example.com/schema.json"}""", "https://example.com/schema.json")]
    [InlineData("""{"$ref":"foo.json#/defs/x"}""", "foo.json#/defs/x")]
    public void MCPP012_FlagsRemoteRefs(string schema, string reference)
    {
        var finding = Single(Lint(schema), "MCPP012");
        Assert.Equal((LintSeverity.Warning, "inputSchema"), (finding.Severity, finding.ParameterName));
        Assert.Contains(reference, finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"$ref":"#/$defs/x","$defs":{"x":{"type":"string"}}}""")]
    [InlineData("""{"$ref":"#/properties/root","properties":{"root":{"type":"string"}}}""")]
    [InlineData("""{"$ref":"#"}""")]
    public void MCPP012_StaysQuietOnLocalRefs(string schema)
    {
        Assert.DoesNotContain(Lint(schema), f => f.RuleId == "MCPP012");
    }

    [Fact]
    public void MCPP012_SkipsTheDocumentWhenASubschemaIdIsSet_ButNotAnIdInExamples()
    {
        var withId = """{"$id":"https://example.com/root","properties":{"city":{"description":"City.","$ref":"https://example.com/other"}}}""";
        Assert.DoesNotContain(Lint(withId), f => f.RuleId == "MCPP012");

        var nestedId = """{"properties":{"city":{"description":"City.","$id":"https://example.com/city","$ref":"https://example.com/other"}}}""";
        Assert.DoesNotContain(Lint(nestedId), f => f.RuleId == "MCPP012");

        var idInExamples = """{"examples":[{"$id":"payload-field"}],"properties":{"city":{"description":"City.","$ref":"https://example.com/other"}}}""";
        Assert.Contains(Lint(idInExamples), f => f.RuleId == "MCPP012" && f.ParameterName == "inputSchema.properties.city");
    }

    [Fact]
    public void Walk_CoversBothSchemas_NestedPaths_AndDefs()
    {
        var input = """{"type":"object","$defs":{"flag":true},"properties":{"city":{"description":"City.","items":{"type":["string","null"]}}}}""";
        var output = """{"type":"object","properties":{"result":{}}}""";
        var findings = Lint(input, outputSchema: output);
        Assert.Contains(findings, f => f.RuleId == "MCPP009" && f.ParameterName == "inputSchema.$defs.flag");
        Assert.Contains(findings, f => f.RuleId == "MCPP010" && f.ParameterName == "inputSchema.properties.city.items");
        Assert.Contains(findings, f => f.RuleId == "MCPP011" && f.ParameterName == "outputSchema.properties.result");
    }

    [Theory]
    [InlineData("""{"type":"array","items":[true,{"type":["string","null"]}]}""", "inputSchema.items[0]", "MCPP009")]
    [InlineData("""{"type":"array","items":[true,{"type":["string","null"]}]}""", "inputSchema.items[1]", "MCPP010")]
    [InlineData("""{"type":"array","prefixItems":[true,{}]}""", "inputSchema.prefixItems[0]", "MCPP009")]
    [InlineData("""{"type":"array","prefixItems":[true,{}]}""", "inputSchema.prefixItems[1]", "MCPP011")]
    [InlineData("""{"type":"string","contentSchema":true}""", "inputSchema.contentSchema", "MCPP009")]
    [InlineData("""{"type":"string","contentSchema":{}}""", "inputSchema.contentSchema", "MCPP011")]
    public void Walk_CoversTupleItems_PrefixItems_AndContentSchema(string schema, string path, string ruleId)
    {
        Assert.Contains(Lint(schema), f => f.RuleId == ruleId && f.ParameterName == path);
    }

    [Fact]
    public void Walk_DeepNesting_DoesNotHang()
    {
        // Stay under System.Text.Json's default parse depth of 64. The walk itself also caps at 64,
        // so a schema that can be loaded as a snapshot is always fully walkable without hanging.
        JsonNode inner = new JsonObject { ["type"] = "string" };
        for (var i = 0; i < 50; i++)
        {
            inner = new JsonObject { ["items"] = inner };
        }

        var findings = Lint(inner.ToJsonString());
        Assert.DoesNotContain(findings, f => f.RuleId is "MCPP009" or "MCPP010" or "MCPP011" or "MCPP012");
    }

    [Fact]
    public void RuleOverride_EnablesOneRuleWithoutStrict_AndCanDisableOneWithStrict()
    {
        var enable = new LintOptions();
        enable.Severities["MCPP009"] = LintSeverity.Error;
        var enabled = Lint("""{"type":"object","properties":{"city":true}}""", enable);
        Assert.Contains(enabled, f => f.RuleId == "MCPP009");
        Assert.DoesNotContain(enabled, f => f.RuleId is "MCPP010" or "MCPP011" or "MCPP012");

        var findings = Lint(UnportableCity, Strict(o => o.Severities["MCPP011"] = LintSeverity.None));
        Assert.Contains(findings, f => f.RuleId == "MCPP009");
        Assert.Contains(findings, f => f.RuleId == "MCPP010");
        Assert.DoesNotContain(findings, f => f.RuleId == "MCPP011");
        Assert.Contains(findings, f => f.RuleId == "MCPP012");
    }

    [Fact]
    public void FailOnWarning_DoesNotFailPortabilityWarnings_ButStillFailsMCPP002()
    {
        var union = ParseSurface("warn", """{"type":["string","null"]}""", GoodDescription);
        var report = ToolLinter.Lint(union, new LintOptions { Strict = true, FailOn = LintSeverity.Warning });
        Assert.Contains(report.Findings, f => f.RuleId == "MCPP010");
        Assert.False(report.Failed);
        report.ThrowIfFailed();

        var shortDescription = ParseSurface("short", """{"type":"object"}""", "Short.");
        var mixed = ToolLinter.Lint(shortDescription, new LintOptions { Strict = true, FailOn = LintSeverity.Warning });
        Assert.Contains(mixed.Findings, f => f.RuleId == "MCPP002");
        Assert.True(mixed.Failed);

        var upgraded = new LintOptions { Strict = true };
        upgraded.Severities["MCPP010"] = LintSeverity.Error;
        var failed = ToolLinter.Lint(union, upgraded);
        Assert.True(failed.Failed);
        Assert.Throws<McpProbeAssertionException>(failed.ThrowIfFailed);

        var boolean = ParseSurface("flag", """{"type":"object","properties":{"city":true}}""", GoodDescription);
        var none = ToolLinter.Lint(boolean, Strict(o => o.FailOn = LintSeverity.None));
        Assert.Contains(none.Findings, f => f.RuleId == "MCPP009");
        Assert.True(none.Failed);
        var error = Assert.Throws<McpProbeAssertionException>(none.ThrowIfFailed);
        Assert.Contains("Lint failed.", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("fail-on: none", error.Message, StringComparison.Ordinal);
        Assert.Contains("MCPP009", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Messages_GoThroughTheSanitizer()
    {
        var schema = """{"type":"object","properties":{"a\u0001b":{"$ref":"https://example.com/a\u0001.json"}}}""";
        var finding = Single(Lint(schema), "MCPP012");
        var text = finding.ToString();
        Assert.DoesNotContain('\u0001', text);
        Assert.Contains("\\u0001", text, StringComparison.Ordinal);
        Assert.StartsWith("inputSchema.properties[", finding.ParameterName);
    }
}
