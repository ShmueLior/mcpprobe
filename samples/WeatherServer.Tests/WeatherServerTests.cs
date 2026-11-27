using McpProbe;
using McpProbe.Assertions;
using ModelContextProtocol.Protocol;

namespace WeatherServer.Tests;

/// <summary>
/// What a test suite for a real MCP server looks like with McpProbe.
/// The server runs in memory; nothing is spawned and no port is opened.
/// </summary>
public sealed class WeatherServerTests : IAsyncLifetime
{
    private McpProbeHost _probe = null!;

    public async ValueTask InitializeAsync() =>
        _probe = await McpProbeHost.StartAsync(mcp => mcp.WithWeatherFeatures(), TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _probe.DisposeAsync();

    [Fact]
    public async Task ListsTheForecastTool()
    {
        var tools = await _probe.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        McpAssert.That(tools)
            .ContainsTool("get_forecast")
            .HasDescription()
            .HasParameter("city", type: "string", required: true)
            .HasParameter("days", type: "integer", required: false)
            .HasOutputSchema()
            .HasReadOnlyHint();
    }

    [Fact]
    public async Task ForecastReturnsStructuredContent()
    {
        var result = await _probe.Client.CallToolAsync(
            "get_forecast",
            new Dictionary<string, object?> { ["city"] = "Tel Aviv", ["days"] = 2 },
            cancellationToken: TestContext.Current.CancellationToken);

        McpAssert.That(result)
            .Succeeded()
            .HasStructuredValue("city", "Tel Aviv")
            .HasStructuredValue("days[0].high", 26)
            .HasStructuredValue("days[1].low", 20);
    }

    [Fact]
    public async Task UnknownCityIsAToolError()
    {
        var result = await _probe.Client.CallToolAsync(
            "get_forecast",
            new Dictionary<string, object?> { ["city"] = "Atlantis" },
            cancellationToken: TestContext.Current.CancellationToken);

        McpAssert.That(result).Failed("Unknown city");
    }

    [Fact]
    public async Task PlanTripPromptMentionsTheCity()
    {
        var ct = TestContext.Current.CancellationToken;
        McpAssert.That(await _probe.Client.ListPromptsAsync(cancellationToken: ct))
            .ContainsPrompt("plan_trip")
            .HasArgument("city", required: true);

        var prompt = await _probe.Client.GetPromptAsync("plan_trip", new Dictionary<string, object?> { ["city"] = "Paris" }, cancellationToken: ct);

        McpAssert.That(prompt).HasTextContent("Paris", Role.User);
    }

    [Fact]
    public async Task CitiesResourceListsEveryCity()
    {
        var ct = TestContext.Current.CancellationToken;
        McpAssert.That(await _probe.Client.ListResourcesAsync(cancellationToken: ct))
            .ContainsResource("weather://cities")
            .HasMimeType("text/plain");

        McpAssert.That(await _probe.Client.ReadResourceAsync("weather://cities", cancellationToken: ct))
            .HasTextContent("Tokyo");
    }

    /// <summary>
    /// Fails when a change would break existing clients (removed tool, removed or newly required
    /// parameter, changed type). The approved surface lives in mcp-tools.json next to this file.
    /// To accept a change: set MCPPROBE_UPDATE_SNAPSHOTS=1, run the tests, review the diff, commit.
    /// </summary>
    [Fact]
    public async Task ToolSurfaceMatchesSnapshot() =>
        await _probe.VerifyToolSnapshotAsync(cancellationToken: TestContext.Current.CancellationToken);

    [Fact]
    public async Task ToolsPassLint()
    {
        var report = await _probe.LintToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        report.ThrowIfFailed();
    }
}
