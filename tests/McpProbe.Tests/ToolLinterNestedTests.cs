using McpProbe.Linting;
using static McpProbe.Tests.TestSupport.SchemaTools;

namespace McpProbe.Tests;

/// <summary>With <see cref="LintOptions.CheckNestedParameters"/>, parameter rules also run on nested object properties and array items.</summary>
public sealed class ToolLinterNestedTests
{
    private const string GoodDescription = "Does a clearly described, useful thing.";

    private static IReadOnlyList<LintFinding> Lint(string inputSchema, LintOptions? options = null) =>
        ToolLinter.Lint([Tool("t", inputSchema, GoodDescription)], options ?? new LintOptions { CheckNestedParameters = true }).Findings;

    [Fact]
    public void ByDefault_OnlyTopLevelParametersAreChecked()
    {
        var schema = """{"type":"object","properties":{"home":{"$ref":"#/$defs/a"},"stops":{"type":"array","description":"Stops.","items":{"type":"object","properties":{"data":{"type":"string"}}}}},"$defs":{"a":{"type":"object","description":"Address.","properties":{"zip":{"type":"string"}}}}}""";

        // Same as 0.1: 'home' has no description of its own (the def's doesn't count), nothing nested is reported.
        var finding = Assert.Single(Lint(schema, new LintOptions()));
        Assert.Equal(("MCPP003", "home"), (finding.RuleId, finding.ParameterName));
        Assert.False(new LintOptions().CheckNestedParameters);

        Assert.Equal(["home.zip", "stops[].data", "stops[].data"], Lint(schema).Select(f => f.ParameterName!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NestedPropertiesAndArrayItems_AreChecked()
    {
        var findings = Lint("""
            {"type":"object","properties":{
              "home":{"type":"object","description":"Home address.","properties":{"zip":{"type":"string"},"city":{"type":"string","description":"City."}}},
              "stops":{"type":"array","description":"Stops.","items":{"type":"object","properties":{"data":{"type":"string","description":"Stop data."}}}}}}
            """);

        Assert.Collection(findings.OrderBy(f => f.ParameterName, StringComparer.Ordinal),
            f => Assert.Equal(("MCPP003", "home.zip", "Property 'home.zip' has no description. Say what it is and what format it expects."), (f.RuleId, f.ParameterName, f.Message)),
            f => Assert.Equal(("MCPP007", "stops[].data", "Property name 'stops[].data' is vague. Use a name that says what the value is (for example 'city' instead of 'value')."), (f.RuleId, f.ParameterName, f.Message)));
    }

    [Fact]
    public void TopLevelMessagesStayTheSame()
    {
        var finding = Assert.Single(Lint("""{"type":"object","properties":{"value":{"type":"string","description":"The value."}}}"""));

        Assert.Equal("Parameter name 'value' is vague. Use a name that says what the value is (for example 'city' instead of 'value').", finding.Message);
    }

    [Fact]
    public void NamingStyle_CountsNestedNames()
    {
        var findings = Lint("""
            {"type":"object","properties":{"trip_plan":{"type":"object","description":"Plan.","properties":{
              "start_date":{"type":"string","description":"Start."},"end_date":{"type":"string","description":"End."},"returnFlight":{"type":"string","description":"Return."}}}}}
            """);

        var finding = Assert.Single(findings);
        Assert.Equal(("MCPP006", "trip_plan.returnFlight"), (finding.RuleId, finding.ParameterName));
    }

    [Fact]
    public void Refs_AreFollowed_AndTheirDescriptionsCount()
    {
        var findings = Lint("""
            {"type":"object","properties":{
              "home":{"$ref":"#/$defs/address"},
              "work":{"$ref":"#/$defs/address","description":"Work address."}},
             "$defs":{"address":{"type":"object","properties":{"zip":{"type":"string"}}}}}
            """);

        // 'home' has no description of its own or in the def; 'work' has one next to the ref.
        // The def is linted once, at the first path that uses it, so 'zip' shows up once.
        Assert.Equal(["home", "home.zip"], findings.Select(f => f.ParameterName!).Order(StringComparer.Ordinal));
        Assert.All(findings, f => Assert.Equal("MCPP003", f.RuleId));
    }

    [Fact]
    public void RecursiveSchemasTheSdkEmits_Terminate()
    {
        // record Node(string Name, List<Node>? Children, Node? Parent), without descriptions.
        var findings = Lint("""{"type":"object","properties":{"root":{"type":"object","description":"Root.","properties":{"name":{"type":"string"},"children":{"type":["array","null"],"items":{"$ref":"#/properties/root"}},"parent":{"type":["object","null"],"properties":{"name":{"type":"string"},"children":{"$ref":"#/properties/root/properties/children","items":{}},"parent":{"$ref":"#/properties/root/properties/parent"}},"required":["name","children","parent"]}},"required":["name","children","parent"]}},"required":["root"]}""");

        Assert.Contains(findings, f => f.ParameterName == "root.name");
        Assert.Contains(findings, f => f.ParameterName == "root.parent.name");
        Assert.True(findings.Count < 20, string.Join("\n", findings));
    }

    [Fact]
    public void RefsThatFanOutALot_FinishQuickly()
    {
        // d0 -> two refs to d1 -> two refs to d2 ... : 2^40 paths if every ref were expanded without a limit.
        var defs = string.Join(",", Enumerable.Range(0, 40).Select(i =>
            "\"d" + i + "\":{\"type\":\"object\",\"description\":\"D.\",\"properties\":{\"l\":{\"$ref\":\"#/$defs/d" + (i + 1) + "\"},\"r\":{\"$ref\":\"#/$defs/d" + (i + 1) + "\"}}}"));
        var schema = "{\"type\":\"object\",\"properties\":{\"root\":{\"$ref\":\"#/$defs/d0\"}},\"$defs\":{" + defs + ",\"d40\":{\"type\":\"string\",\"description\":\"Leaf.\"}}}";

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var findings = Lint(schema);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), watch.Elapsed.ToString());
        Assert.Empty(findings);
    }

    [Fact]
    public void RulesTurnedOff_DoNotReportNestedFindings()
    {
        var options = new LintOptions { CheckNestedParameters = true };
        options.Severities[LintRules.ParameterMissingDescription.Id] = LintSeverity.None;

        Assert.Empty(Lint("""{"type":"object","properties":{"home":{"type":"object","properties":{"zip":{"type":"string"}}}}}""", options));
    }
}
