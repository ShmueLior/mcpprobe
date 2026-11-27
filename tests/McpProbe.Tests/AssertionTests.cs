using McpProbe.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using WeatherServer;

namespace McpProbe.Tests;

public sealed class AssertionTests : IAsyncLifetime
{
    private McpProbeHost _probe = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        _probe = await McpProbeHost.StartAsync(
            mcp => mcp.WithWeatherFeatures().WithTools<TestTools>(),
            new McpProbeHostOptions { ConfigureServices = s => s.AddSingleton<GreetingService>() },
            Ct);

    public ValueTask DisposeAsync() => _probe.DisposeAsync();

    private async Task<CallToolResult> CallAsync(string tool, Dictionary<string, object?>? args = null) =>
        await _probe.Client.CallToolAsync(tool, args, cancellationToken: Ct);

    [Fact]
    public async Task ContainsTool_PassesAndChains()
    {
        var tools = await _probe.Client.ListToolsAsync(cancellationToken: Ct);

        McpAssert.That(tools)
            .ContainsTool("get_forecast")
            .HasDescription("forecast")
            .HasParameter("city", "string", required: true)
            .HasParameter("days", "integer", required: false)
            .DoesNotHaveParameter("country")
            .HasOutputSchema()
            .HasReadOnlyHint();
    }

    [Fact]
    public async Task ContainsTool_FailureListsToolsAndHintsAtCase()
    {
        var tools = await _probe.Client.ListToolsAsync(cancellationToken: Ct);

        var error = Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(tools).ContainsTool("GET_FORECAST"));

        Assert.Contains("Expected a tool named 'GET_FORECAST'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Did you mean 'get_forecast'?", error.Message, StringComparison.Ordinal);
        Assert.Contains("'convert_temperature'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolListAssertions_CountAndAbsence()
    {
        var tools = await _probe.Client.ListToolsAsync(cancellationToken: Ct);

        McpAssert.That(tools).HasCount(6).DoesNotContainTool("delete_city");
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(tools).HasCount(2));
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(tools).DoesNotContainTool("get_alerts"));
    }

    [Theory]
    [InlineData("city", "integer", null, "to be of type 'integer', but it is string")]
    [InlineData("days", null, true, "to be required, but it is optional")]
    [InlineData("city", null, false, "to be optional, but it is required")]
    [InlineData("zip", null, null, "to have a parameter 'zip', but it doesn't. Parameters: 'city', 'days'")]
    public async Task HasParameter_FailuresExplainWhatWasFound(string name, string? type, bool? required, string expected)
    {
        var tools = await _probe.Client.ListToolsAsync(cancellationToken: Ct);
        var tool = McpAssert.That(tools).ContainsTool("get_forecast");

        var error = Assert.Throws<McpProbeAssertionException>(() => tool.HasParameter(name, type, required));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolAssertions_OtherFailures()
    {
        var tools = await _probe.Client.ListToolsAsync(cancellationToken: Ct);
        var alerts = McpAssert.That(tools).ContainsTool("get_alerts");

        Assert.Throws<McpProbeAssertionException>(() => alerts.HasOutputSchema());
        Assert.Throws<McpProbeAssertionException>(() => alerts.HasDescription("forecast"));
        Assert.Throws<McpProbeAssertionException>(() => alerts.DoesNotHaveParameter("city"));
        Assert.Throws<McpProbeAssertionException>(() => alerts.HasReadOnlyHint(false));

        // destructiveHint defaults to true when unset, per the MCP spec.
        var greet = McpAssert.That(tools).ContainsTool("greet").HasDestructiveHint();
        var error = Assert.Throws<McpProbeAssertionException>(() => greet.HasDestructiveHint(false));
        Assert.Contains("unset (defaults to true)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HasDescription_FailsWhenMissing()
    {
        var tool = SchemaTools.Tool("blank", description: "   ");

        var error = Assert.Throws<McpProbeAssertionException>(() => McpAssert.That([tool]).ContainsTool("blank").HasDescription());

        Assert.Contains("to have a description", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallResult_SucceededWithTextAndStructuredContent()
    {
        var result = await CallAsync("get_forecast", new() { ["city"] = "Paris", ["days"] = 2 });

        McpAssert.That(result)
            .Succeeded()
            .HasStructuredContent()
            .HasStructuredValue("city", "Paris")
            .HasStructuredValue("days[0].day", 1)
            .HasStructuredValue("days[1].high", 18L)
            .HasStructuredValue("days[1].low", 11.0)
            .HasStructuredValue("days[0].summary", "Partly cloudy")
            .HasTextContent("Paris");
    }

    [Fact]
    public async Task CallResult_StructuredValueMismatchAndMissingPaths()
    {
        var result = McpAssert.That(await CallAsync("get_forecast", new() { ["city"] = "Paris", ["days"] = 1 }));

        Assert.Contains("Expected structured content 'city' to be \"London\", but it is \"Paris\"",
            Assert.Throws<McpProbeAssertionException>(() => result.HasStructuredValue("city", "London")).Message, StringComparison.Ordinal);
        Assert.Contains("has no property 'country'. Properties at '(root)': 'city', 'days'",
            Assert.Throws<McpProbeAssertionException>(() => result.HasStructuredValue("country", "FR")).Message, StringComparison.Ordinal);
        Assert.Contains("has no element 'days[5]'",
            Assert.Throws<McpProbeAssertionException>(() => result.HasStructuredValue("days[5].high", 1)).Message, StringComparison.Ordinal);
        Assert.Throws<McpProbeAssertionException>(() => result.HasStructuredValue("days[0].high", true));
        Assert.Throws<McpProbeAssertionException>(() => result.HasStructuredValue("city", (string?)null));
        Assert.Throws<McpProbeAssertionException>(() => result.HasStructuredValue("days[0].high", 99.5, tolerance: 0.1));
        Assert.Throws<ArgumentException>(() => result.HasStructuredValue("days[x]", 1));
        Assert.Throws<ArgumentException>(() => result.HasStructuredValue("city..name", 1));
    }

    [Fact]
    public void CallResult_StructuredBooleansNullsAndTolerance()
    {
        var result = new CallToolResult
        {
            StructuredContent = SchemaTools.Parse("""{"ok":true,"missing":null,"temp":21.04,"big":12345678901234}"""),
        };

        McpAssert.That(result)
            .HasStructuredValue("ok", true)
            .HasStructuredValue("missing", (string?)null)
            .HasStructuredValue("temp", 21.0, tolerance: 0.05)
            .HasStructuredValue("big", 12345678901234L);
    }

    [Fact]
    public async Task CallResult_ToolErrorIsReportedAsFailure()
    {
        var result = await CallAsync("get_forecast", new() { ["city"] = "Atlantis" });

        McpAssert.That(result).Failed("Unknown city 'Atlantis'");
        var error = Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(result).Succeeded());
        Assert.Contains("returned an error", error.Message, StringComparison.Ordinal);
        Assert.Contains("Atlantis", error.Message, StringComparison.Ordinal);
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(result).Failed("something else"));
    }

    [Fact]
    public async Task CallResult_FailedOnSuccessfulCallThrows()
    {
        var result = await CallAsync("get_alerts", new() { ["city"] = "London" });

        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(result).Failed());
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(result).HasStructuredContent());
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(result).HasTextContent("Heat"));
        McpAssert.That(result).HasTextContent("no active alerts", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallResult_ContentTypes()
    {
        var image = await CallAsync("get_image");
        var text = await CallAsync("get_text_and_data");

        McpAssert.That(image).Succeeded().HasImageContent("image/png").HasContent<ImageContentBlock>(b => b.DecodedData.Length > 0);
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(image).HasImageContent("image/jpeg"));
        Assert.Contains("content blocks of type 'image'",
            Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(image).HasTextContent()).Message, StringComparison.Ordinal);

        McpAssert.That(text).HasTextContent("second").HasContent<TextContentBlock>(b => b.Text == "first");
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(text).HasContent<TextContentBlock>(b => b.Text == "third"));
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(text).HasContent<EmbeddedResourceBlock>());
        Assert.Contains("no content blocks",
            Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(new CallToolResult()).HasImageContent()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prompts_ListAndGet()
    {
        var prompts = await _probe.Client.ListPromptsAsync(cancellationToken: Ct);

        McpAssert.That(prompts)
            .ContainsPrompt("plan_trip")
            .HasDescription("pack")
            .HasArgument("city", required: true)
            .HasArgument("days", required: false);
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(prompts).ContainsPrompt("missing"));
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(prompts).ContainsPrompt("plan_trip").HasArgument("budget"));
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(prompts).ContainsPrompt("plan_trip").HasArgument("days", required: true));
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(prompts).ContainsPrompt("plan_trip").HasDescription("weather radar"));

        var result = await _probe.Client.GetPromptAsync("plan_trip", new Dictionary<string, object?> { ["city"] = "Tokyo", ["days"] = 4 }, cancellationToken: Ct);

        McpAssert.That(result).HasMessageCount(1).HasTextContent("Tokyo for 4 days", Role.User);
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(result).HasMessageCount(2));
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(result).HasTextContent("Tokyo", Role.Assistant));
    }

    [Fact]
    public async Task Resources_ListAndRead()
    {
        var resources = await _probe.Client.ListResourcesAsync(cancellationToken: Ct);

        McpAssert.That(resources)
            .ContainsResource("weather://cities")
            .HasName("cities")
            .HasMimeType("text/plain")
            .HasDescription();
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(resources).ContainsResource("weather://nope"));
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(resources).ContainsResource("weather://cities").HasName("towns"));
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(resources).ContainsResource("weather://cities").HasMimeType("application/json"));

        var read = await _probe.Client.ReadResourceAsync("weather://cities", cancellationToken: Ct);

        McpAssert.That(read).HasTextContent("Tel Aviv").HasMimeType("text/plain");
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(read).HasTextContent("Atlantis"));
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(read).HasMimeType("text/html"));
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(new ReadResourceResult()).HasTextContent());
        Assert.Throws<McpProbeAssertionException>(() => McpAssert.That(new ReadResourceResult()).HasMimeType("text/plain"));
    }

    [Fact]
    public void That_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => McpAssert.That((CallToolResult)null!));
        Assert.Throws<ArgumentNullException>(() => McpAssert.That((IEnumerable<Tool>)null!));
    }

    [Fact]
    public void AssertionException_IsAPlainException()
    {
        // Framework-agnostic by design: no dependency on any test framework's exception types.
        Assert.Equal(typeof(Exception), typeof(McpProbeAssertionException).BaseType);
        var references = typeof(McpAssert).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        Assert.DoesNotContain(references, n => n.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("nunit", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("Microsoft.VisualStudio.TestPlatform", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("MSTest", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("TUnit", StringComparison.OrdinalIgnoreCase));
    }
}
