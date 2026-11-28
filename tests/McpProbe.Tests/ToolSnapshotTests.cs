using System.Runtime.CompilerServices;
using McpProbe.Snapshots;
using McpProbe.Tests.TestSupport;
using ModelContextProtocol.Client;
using WeatherServer;
using static McpProbe.Tests.TestSupport.SchemaTools;

namespace McpProbe.Tests;

public sealed class ToolSnapshotTests : IDisposable
{
    private const string CityRequired = """{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}""";

    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    // Explicit Update/CreateMissing values keep these tests independent of the CI and MCPPROBE_UPDATE_SNAPSHOTS variables.
    private SnapshotOptions Local(Action<SnapshotOptions>? configure = null)
    {
        var options = new SnapshotOptions { Directory = _dir.Path, Update = false, CreateMissing = true };
        configure?.Invoke(options);
        return options;
    }

    private static ToolSurface Baseline => Surface(Tool("get_forecast", CityRequired), Tool("get_alerts"));

    private void WriteBaseline() => File.WriteAllText(_dir.File(ToolSnapshot.DefaultFileName), Baseline.ToJson());

    [Fact]
    public void MissingSnapshot_IsWrittenAndFailsOnce()
    {
        var error = Assert.Throws<McpProbeSnapshotException>(() => ToolSnapshot.Verify(Baseline, options: Local()));

        Assert.Contains("No tool snapshot existed, so one was written", error.Message, StringComparison.Ordinal);
        Assert.Equal(_dir.File(ToolSnapshot.DefaultFileName), error.SnapshotPath);
        Assert.Equal(Baseline.ToJson(), File.ReadAllText(_dir.File(ToolSnapshot.DefaultFileName)));

        // Second run passes.
        Assert.False(ToolSnapshot.Verify(Baseline, options: Local()).HasChanges);
    }

    [Fact]
    public void MissingSnapshot_OnCi_FailsWithoutWritingIt()
    {
        var error = Assert.Throws<McpProbeSnapshotException>(() => ToolSnapshot.Verify(Baseline, options: Local(o => o.CreateMissing = false)));

        Assert.Contains("Tool snapshot not found", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(_dir.File("mcp-tools.json")));
        Assert.True(File.Exists(_dir.File("mcp-tools.received.json")));
    }

    [Fact]
    public void MatchingSnapshot_PassesAndCleansUpReceivedFile()
    {
        WriteBaseline();
        File.WriteAllText(_dir.File("mcp-tools.received.json"), "stale");

        var diff = ToolSnapshot.Verify(Baseline, options: Local());

        Assert.False(diff.HasChanges);
        Assert.False(File.Exists(_dir.File("mcp-tools.received.json")));
    }

    [Fact]
    public void BreakingChange_FailsWithDiffAndWritesReceivedFile()
    {
        WriteBaseline();
        var current = Surface(Tool("get_forecast", """{"type":"object","properties":{"location":{"type":"string"}},"required":["location"]}"""));

        var error = Assert.Throws<McpProbeSnapshotException>(() => ToolSnapshot.Verify(current, options: Local()));

        Assert.Contains("has breaking changes", error.Message, StringComparison.Ordinal);
        Assert.Contains("- get_alerts: Tool was removed.", error.Message, StringComparison.Ordinal);
        Assert.Contains("Parameter 'city' was removed (possibly renamed to 'location').", error.Message, StringComparison.Ordinal);
        Assert.Contains(SnapshotOptions.UpdateEnvironmentVariable + "=1", error.Message, StringComparison.Ordinal);
        Assert.True(error.Diff!.HasBreakingChanges);
        Assert.Equal(current.ToJson(), File.ReadAllText(_dir.File("mcp-tools.received.json")));
        Assert.Equal(Baseline.ToJson(), File.ReadAllText(_dir.File("mcp-tools.json")));
    }

    [Fact]
    public void NonBreakingChange_PassesAndIsLogged()
    {
        WriteBaseline();
        var current = Surface(Tool("get_forecast", CityRequired), Tool("get_alerts"), Tool("get_radar"));
        var logs = new List<string>();

        var diff = ToolSnapshot.Verify(current, options: Local(o => o.Log = logs.Add));

        Assert.True(diff.HasChanges);
        Assert.False(diff.HasBreakingChanges);
        var log = Assert.Single(logs);
        Assert.Contains("~ get_radar: Tool was added.", log, StringComparison.Ordinal);
        Assert.Equal(Baseline.ToJson(), File.ReadAllText(_dir.File("mcp-tools.json")));
    }

    [Fact]
    public void NonBreakingChange_FailsWhenConfigured()
    {
        WriteBaseline();
        var current = Surface(Tool("get_forecast", CityRequired), Tool("get_alerts"), Tool("get_radar"));

        var error = Assert.Throws<McpProbeSnapshotException>(() => ToolSnapshot.Verify(current, options: Local(o => o.FailOnNonBreakingChanges = true)));

        Assert.Contains("The tool surface changed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateMode_RewritesTheSnapshotAndPasses()
    {
        WriteBaseline();
        var current = Surface(Tool("only_tool"));

        var diff = ToolSnapshot.Verify(current, options: Local(o => o.Update = true));

        Assert.True(diff.HasBreakingChanges);
        Assert.Equal(current.ToJson(), File.ReadAllText(_dir.File("mcp-tools.json")));
        Assert.False(ToolSnapshot.Verify(current, options: Local()).HasChanges);
    }

    [Fact]
    public void UpdateMode_CreatesMissingSnapshotWithoutFailing()
    {
        var diff = ToolSnapshot.Verify(Baseline, options: Local(o => { o.Update = true; o.CreateMissing = false; }));

        Assert.False(diff.HasChanges);
        Assert.True(File.Exists(_dir.File("mcp-tools.json")));
    }

    [Fact]
    public void CorruptSnapshot_FailsClearly_AndUpdateRepairsIt()
    {
        File.WriteAllText(_dir.File("mcp-tools.json"), "{ not json");

        var error = Assert.Throws<McpProbeSnapshotException>(() => ToolSnapshot.Verify(Baseline, options: Local()));
        Assert.Contains("could not be read", error.Message, StringComparison.Ordinal);

        ToolSnapshot.Verify(Baseline, options: Local(o => o.Update = true));
        Assert.Equal(Baseline.ToJson(), File.ReadAllText(_dir.File("mcp-tools.json")));
    }

    [Fact]
    public void ReformattedButEquivalentSnapshot_Passes()
    {
        File.WriteAllText(_dir.File("mcp-tools.json"), Baseline.ToJson().Replace("\n", "\r\n", StringComparison.Ordinal).Replace("  ", "    ", StringComparison.Ordinal));

        Assert.False(ToolSnapshot.Verify(Baseline, options: Local()).HasChanges);
    }

    [Fact]
    public void RelativePath_ResolvesAgainstCallerDirectory()
    {
        var caller = _dir.File("MyTests.cs");

        Assert.Equal(_dir.File(Path.Combine("snapshots", "a.json")), ToolSnapshot.ResolvePath(Path.Combine("snapshots", "a.json"), null, caller));
        Assert.Equal(_dir.File("abs.json"), ToolSnapshot.ResolvePath(_dir.File("abs.json"), null, "/does/not/matter.cs"));
        Assert.Equal(Path.GetFullPath(Path.Combine("/elsewhere", "a.json")), ToolSnapshot.ResolvePath("a.json", new SnapshotOptions { Directory = "/elsewhere" }, caller));
    }

    [Fact]
    public void RelativePath_WithMappedCallerPath_ThrowsHelpfully()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ToolSnapshot.ResolvePath("a.json", null, "/_/tests/NoSuchProject/MyTests.cs", _dir.Path));

        Assert.Contains("ContinuousIntegrationBuild", error.Message, StringComparison.Ordinal);
        Assert.Contains("SnapshotOptions.Directory", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvePath_FindsSourcesBehindDeterministicPathMapping()
    {
        var project = Path.Combine(_dir.Path, "repo", "tests", "My.Tests");
        var binaries = Path.Combine(project, "bin", "Release", "net10.0");
        Directory.CreateDirectory(binaries);
        File.WriteAllText(Path.Combine(project, "MyTests.cs"), "// test");

        Assert.Equal(Path.Combine(project, "mcp-tools.json"), ToolSnapshot.ResolvePath("mcp-tools.json", null, "/_/tests/My.Tests/MyTests.cs", binaries));
        Assert.Equal(Path.Combine(project, "mcp-tools.json"), ToolSnapshot.ResolvePath("mcp-tools.json", null, "/_1/tests/My.Tests/MyTests.cs", binaries));
    }

    [Fact]
    public void SubdirectoriesAreCreated()
    {
        var options = Local();
        var path = Path.Combine("nested", "deeper", "tools.json");

        Assert.Throws<McpProbeSnapshotException>(() => ToolSnapshot.Verify(Baseline, path, options));

        Assert.True(File.Exists(_dir.File(path)));
        Assert.Equal(_dir.File(Path.Combine("nested", "deeper", "tools.received.json")), ToolSnapshot.GetReceivedPath(_dir.File(path)));
    }

    [Fact]
    public async Task EndToEnd_WithInMemoryHost()
    {
        await using var probe = await McpProbeHost.StartAsync(mcp => mcp.WithWeatherFeatures(), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<McpProbeSnapshotException>(() => probe.VerifyToolSnapshotAsync(options: Local(), cancellationToken: TestContext.Current.CancellationToken));
        var diff = await probe.VerifyToolSnapshotAsync(options: Local(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(diff.HasChanges);
        Assert.Contains("\"get_forecast\"", File.ReadAllText(_dir.File("mcp-tools.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OptionsFirst_HostVerify_UsesDefaultFileNameAndHonorsOptions()
    {
        await using var probe = await McpProbeHost.StartAsync(mcp => mcp.WithWeatherFeatures(), TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<McpProbeSnapshotException>(() =>
            probe.VerifyToolSnapshotAsync(Local(), TestContext.Current.CancellationToken));

        Assert.Equal(_dir.File(ToolSnapshot.DefaultFileName), error.SnapshotPath);
        Assert.True(File.Exists(_dir.File(ToolSnapshot.DefaultFileName)));
        Assert.Contains("\"get_forecast\"", File.ReadAllText(_dir.File(ToolSnapshot.DefaultFileName)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OptionsFirst_HostVerify_ResolvesPathFromTestFileNotHostSource()
    {
        var expected = ToolSnapshot.ResolvePath(ToolSnapshot.DefaultFileName, options: null, callerFilePath: ThisFile());
        var received = ToolSnapshot.GetReceivedPath(expected);
        try
        {
            await using var probe = await McpProbeHost.StartAsync(mcp => mcp.WithWeatherFeatures(), TestContext.Current.CancellationToken);
            var options = new SnapshotOptions { Update = false, CreateMissing = false };

            var error = await Assert.ThrowsAsync<McpProbeSnapshotException>(() =>
                probe.VerifyToolSnapshotAsync(options, TestContext.Current.CancellationToken));

            Assert.Equal(expected, error.SnapshotPath);
            Assert.DoesNotContain("McpProbeHost.cs", error.SnapshotPath, StringComparison.Ordinal);
            Assert.Contains(Path.Combine("tests", "McpProbe.Tests"), error.SnapshotPath, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(expected))
            {
                File.Delete(expected);
            }

            if (File.Exists(received))
            {
                File.Delete(received);
            }
        }
    }

    [Fact]
    public void OptionsFirst_Verify_UsesDefaultFileNameAndHonorsOptions()
    {
        var error = Assert.Throws<McpProbeSnapshotException>(() => ToolSnapshot.Verify(Baseline, Local()));

        Assert.Equal(_dir.File(ToolSnapshot.DefaultFileName), error.SnapshotPath);
        Assert.Equal(Baseline.ToJson(), File.ReadAllText(_dir.File(ToolSnapshot.DefaultFileName)));
    }

    [Fact]
    public async Task OptionsFirst_VerifyAsync_UsesDefaultFileNameAndHonorsOptions()
    {
        await using var probe = await McpProbeHost.StartAsync(mcp => mcp.WithWeatherFeatures(), TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<McpProbeSnapshotException>(() =>
            ToolSnapshot.VerifyAsync(probe.Client, Local(), TestContext.Current.CancellationToken));

        Assert.Equal(_dir.File(ToolSnapshot.DefaultFileName), error.SnapshotPath);
        Assert.True(File.Exists(_dir.File(ToolSnapshot.DefaultFileName)));
    }

    [Fact]
    public void OptionsFirst_Verify_NullOptions_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => ToolSnapshot.Verify(Baseline, (SnapshotOptions)null!));
        Assert.Equal("snapshotOptions", ex.ParamName);
    }

    [Fact]
    public async Task OptionsFirst_VerifyAsync_NullOptions_Throws()
    {
        await using var probe = await McpProbeHost.StartAsync(mcp => mcp.WithWeatherFeatures(), TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ToolSnapshot.VerifyAsync(probe.Client, (SnapshotOptions)null!, TestContext.Current.CancellationToken));
        Assert.Equal("snapshotOptions", ex.ParamName);
    }

    [Fact]
    public async Task OptionsFirst_HostVerify_NullOptions_Throws()
    {
        await using var probe = await McpProbeHost.StartAsync(mcp => mcp.WithWeatherFeatures(), TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            probe.VerifyToolSnapshotAsync((SnapshotOptions)null!, TestContext.Current.CancellationToken));
        Assert.Equal("snapshotOptions", ex.ParamName);
    }

    [Fact]
    public void NamedOptions_Verify_UsesDefaultFileNameAndHonorsOptions()
    {
        var error = Assert.Throws<McpProbeSnapshotException>(() => ToolSnapshot.Verify(Baseline, options: Local()));

        Assert.Equal(_dir.File(ToolSnapshot.DefaultFileName), error.SnapshotPath);
        Assert.Equal(Baseline.ToJson(), File.ReadAllText(_dir.File(ToolSnapshot.DefaultFileName)));
    }

    [Fact]
    public async Task NamedOptions_VerifyAsync_UsesDefaultFileNameAndHonorsOptions()
    {
        await using var probe = await McpProbeHost.StartAsync(mcp => mcp.WithWeatherFeatures(), TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<McpProbeSnapshotException>(() =>
            ToolSnapshot.VerifyAsync(probe.Client, options: Local(), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(_dir.File(ToolSnapshot.DefaultFileName), error.SnapshotPath);
        Assert.True(File.Exists(_dir.File(ToolSnapshot.DefaultFileName)));
    }

    [Fact]
    public async Task NamedOptions_HostVerify_UsesDefaultFileNameAndHonorsOptions()
    {
        await using var probe = await McpProbeHost.StartAsync(mcp => mcp.WithWeatherFeatures(), TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<McpProbeSnapshotException>(() =>
            probe.VerifyToolSnapshotAsync(options: Local(), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(_dir.File(ToolSnapshot.DefaultFileName), error.SnapshotPath);
        Assert.True(File.Exists(_dir.File(ToolSnapshot.DefaultFileName)));
        Assert.Contains("\"get_forecast\"", File.ReadAllText(_dir.File(ToolSnapshot.DefaultFileName)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NamedOptions_HostVerify_ResolvesPathFromTestFile()
    {
        var expected = ToolSnapshot.ResolvePath(ToolSnapshot.DefaultFileName, options: null, callerFilePath: ThisFile());
        var received = ToolSnapshot.GetReceivedPath(expected);
        try
        {
            await using var probe = await McpProbeHost.StartAsync(mcp => mcp.WithWeatherFeatures(), TestContext.Current.CancellationToken);
            var options = new SnapshotOptions { Update = false, CreateMissing = false };

            var error = await Assert.ThrowsAsync<McpProbeSnapshotException>(() =>
                probe.VerifyToolSnapshotAsync(options: options, cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(expected, error.SnapshotPath);
            Assert.DoesNotContain("McpProbeHost.cs", error.SnapshotPath, StringComparison.Ordinal);
            Assert.Contains(Path.Combine("tests", "McpProbe.Tests"), error.SnapshotPath, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(expected))
            {
                File.Delete(expected);
            }

            if (File.Exists(received))
            {
                File.Delete(received);
            }
        }
    }

    [Fact]
    public void OverloadResolution_CallShapes_Compile()
    {
        // The body of CompileOverloadResolutionMatrix is the compile matrix: if any shape is
        // CS0121 or binds to the wrong parameter type, this test project does not build.
        Assert.NotNull((Action<McpProbeHost, McpClient, ToolSurface, SnapshotOptions, CancellationToken>)CompileOverloadResolutionMatrix);
    }

    private static void CompileOverloadResolutionMatrix(
        McpProbeHost probe,
        McpClient client,
        ToolSurface surface,
        SnapshotOptions snapshotOptions,
        CancellationToken cancellationToken)
    {
        // Named options: only, the 0.2/0.3 workaround. Must bind to the path-first overloads.
        _ = probe.VerifyToolSnapshotAsync(options: snapshotOptions);
        _ = probe.VerifyToolSnapshotAsync(options: snapshotOptions, cancellationToken: cancellationToken);
        _ = ToolSnapshot.Verify(surface, options: snapshotOptions);
        _ = ToolSnapshot.VerifyAsync(client, options: snapshotOptions);
        _ = ToolSnapshot.VerifyAsync(client, options: snapshotOptions, cancellationToken: cancellationToken);

        // Positional options. Must bind to the options-first overloads.
        _ = probe.VerifyToolSnapshotAsync(snapshotOptions);
        _ = probe.VerifyToolSnapshotAsync(snapshotOptions, cancellationToken);
        _ = ToolSnapshot.Verify(surface, snapshotOptions);
        _ = ToolSnapshot.VerifyAsync(client, snapshotOptions);
        _ = ToolSnapshot.VerifyAsync(client, snapshotOptions, cancellationToken);

        // Path only.
        _ = probe.VerifyToolSnapshotAsync("custom.json");
        _ = ToolSnapshot.Verify(surface, "custom.json");
        _ = ToolSnapshot.VerifyAsync(client, "custom.json");

        // No-arg (default path, default options).
        _ = probe.VerifyToolSnapshotAsync();
        _ = ToolSnapshot.Verify(surface);
        _ = ToolSnapshot.VerifyAsync(client);

        // cancellationToken: only, as used by samples/WeatherServer.Tests.
        _ = probe.VerifyToolSnapshotAsync(cancellationToken: cancellationToken);
        _ = ToolSnapshot.VerifyAsync(client, cancellationToken: cancellationToken);

        // Path plus options, positional and named.
        _ = probe.VerifyToolSnapshotAsync("custom.json", snapshotOptions);
        _ = ToolSnapshot.Verify(surface, "custom.json", snapshotOptions);
        _ = ToolSnapshot.VerifyAsync(client, "custom.json", snapshotOptions);
        _ = probe.VerifyToolSnapshotAsync("custom.json", options: snapshotOptions);
        _ = ToolSnapshot.Verify(surface, snapshotPath: "custom.json", options: snapshotOptions);

        // Explicit name of the new parameter.
        _ = probe.VerifyToolSnapshotAsync(snapshotOptions: snapshotOptions);
        _ = ToolSnapshot.Verify(surface, snapshotOptions: snapshotOptions);
        _ = ToolSnapshot.VerifyAsync(client, snapshotOptions: snapshotOptions);
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;
}

[CollectionDefinition(nameof(EnvironmentVariableTests), DisableParallelization = true)]
public sealed class EnvironmentVariableGroup;

[Collection(nameof(EnvironmentVariableTests))]
public sealed class EnvironmentVariableTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void UpdateMode_FollowsEnvironmentVariable(string? value, bool expected)
    {
        var previous = Environment.GetEnvironmentVariable(SnapshotOptions.UpdateEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(SnapshotOptions.UpdateEnvironmentVariable, value);

            Assert.Equal(expected, new SnapshotOptions().ResolveUpdate());
            Assert.False(new SnapshotOptions { Update = false }.ResolveUpdate());
        }
        finally
        {
            Environment.SetEnvironmentVariable(SnapshotOptions.UpdateEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void CreateMissing_IsOffOnCi()
    {
        var previousCi = Environment.GetEnvironmentVariable("CI");
        var previousTf = Environment.GetEnvironmentVariable("TF_BUILD");
        try
        {
            Environment.SetEnvironmentVariable("TF_BUILD", null);
            Environment.SetEnvironmentVariable("CI", "true");
            Assert.False(new SnapshotOptions().ResolveCreateMissing());

            Environment.SetEnvironmentVariable("CI", null);
            Assert.True(new SnapshotOptions().ResolveCreateMissing());

            Environment.SetEnvironmentVariable("TF_BUILD", "True");
            Assert.False(new SnapshotOptions().ResolveCreateMissing());
            Assert.True(new SnapshotOptions { CreateMissing = true }.ResolveCreateMissing());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CI", previousCi);
            Environment.SetEnvironmentVariable("TF_BUILD", previousTf);
        }
    }
}
