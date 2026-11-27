using McpProbe.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WeatherServer;

namespace McpProbe.Tests;

public sealed class McpProbeHostTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StartAsync_ConnectsAClientThatSeesTheServersTools()
    {
        await using var probe = await McpProbeHost.StartAsync(mcp => mcp.WithWeatherFeatures(), Ct);

        var tools = await probe.Client.ListToolsAsync(cancellationToken: Ct);

        Assert.Equal(["convert_temperature", "get_alerts", "get_forecast"], tools.Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task StartAsync_CallsToolsOverTheRealProtocol()
    {
        await using var probe = await McpProbeHost.StartAsync(mcp => mcp.WithWeatherFeatures(), Ct);

        var result = await probe.Client.CallToolAsync("get_alerts", new Dictionary<string, object?> { ["city"] = "Tel Aviv" }, cancellationToken: Ct);

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("Heat advisory", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_ResolvesToolDependenciesAndLoggerWithoutLoggingRegistered()
    {
        await using var probe = await McpProbeHost.StartAsync(
            mcp => mcp.WithTools<TestTools>(),
            new McpProbeHostOptions { ConfigureServices = services => services.AddSingleton<GreetingService>() },
            Ct);

        var result = await probe.Client.CallToolAsync("greet", new Dictionary<string, object?> { ["name"] = "Lior" }, cancellationToken: Ct);

        McpAssert.That(result).Succeeded().HasTextContent("Hello, Lior!");
    }

    [Fact]
    public async Task StartAsync_AppliesServerOptions()
    {
        await using var probe = await McpProbeHost.StartAsync(
            mcp => mcp.WithWeatherFeatures(),
            new McpProbeHostOptions
            {
                ConfigureServer = o =>
                {
                    o.ServerInfo = new Implementation { Name = "probe-test", Version = "9.9.9" };
                    o.ServerInstructions = "Use get_forecast for forecasts.";
                },
            },
            Ct);

        Assert.NotNull(probe.Client.ServerInfo);
        Assert.Equal("probe-test", probe.Client.ServerInfo.Name);
        Assert.Equal("9.9.9", probe.Client.ServerInfo.Version);
        Assert.Equal("Use get_forecast for forecasts.", probe.Client.ServerInstructions);
    }

    [Fact]
    public async Task Services_ExposesTheServersServiceProvider()
    {
        var service = new GreetingService();
        await using var probe = await McpProbeHost.StartAsync(
            mcp => mcp.WithTools<TestTools>(),
            new McpProbeHostOptions { ConfigureServices = services => services.AddSingleton(service) },
            Ct);

        Assert.Same(service, probe.Services.GetRequiredService<GreetingService>());
        Assert.Same(probe.Server, probe.Services.GetRequiredService<McpServer>());
    }

    [Fact]
    public async Task StartAsync_PropagatesConfigurationErrors()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            McpProbeHost.StartAsync(_ => throw new InvalidOperationException("bad config"), Ct));

        Assert.Equal("bad config", error.Message);
    }

    [Fact]
    public async Task StartAsync_RejectsNullConfigure()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => McpProbeHost.StartAsync(null!, Ct));
    }

    [Fact]
    public async Task StartAsync_HonorsCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => McpProbeHost.StartAsync(mcp => mcp.WithWeatherFeatures(), cts.Token));
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotentAndStopsTheServer()
    {
        var probe = await McpProbeHost.StartAsync(mcp => mcp.WithWeatherFeatures(), Ct);
        var client = probe.Client;

        await probe.DisposeAsync();
        await probe.DisposeAsync();

        await Assert.ThrowsAnyAsync<Exception>(async () => await client.ListToolsAsync(cancellationToken: Ct));
    }

    [Fact]
    public async Task ManyHostsCanRunInParallel()
    {
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            await using var probe = await McpProbeHost.StartAsync(mcp => mcp.WithWeatherFeatures(), Ct);
            var result = await probe.Client.CallToolAsync("convert_temperature", new Dictionary<string, object?> { ["degrees"] = i, ["from"] = "Celsius" }, cancellationToken: Ct);
            return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        });

        var results = await Task.WhenAll(tasks);

        Assert.Equal("32°F", results[0]);
        Assert.Equal("41°F", results[5]);
        Assert.Equal("48.2°F", results[9]);
    }
}
