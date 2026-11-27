using McpProbe.Cli;
using McpProbe.Snapshots;
using McpProbe.Tests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WeatherServer;
using static McpProbe.Tests.TestSupport.SchemaTools;

namespace McpProbe.Tests;

public sealed class CliTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    /// <summary>
    /// Runs the sample server from its own build output (it is a project reference, so it is built first).
    /// Its copy in the test output folder doesn't have all of its own dependencies.
    /// </summary>
    private static string StdioCommand { get; } = BuildStdioCommand();

    private static string BuildStdioCommand()
    {
        // .../tests/McpProbe.Tests/bin/<Configuration>/<TargetFramework>/
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var framework = testOutput.Name;
        var configuration = testOutput.Parent!.Name;
        var repoRoot = testOutput.Parent.Parent!.Parent!.Parent!.Parent!.FullName;
        var dll = Path.Combine(repoRoot, "samples", "WeatherServer", "bin", configuration, framework, "WeatherServer.dll");
        Assert.True(File.Exists(dll), $"Sample server not built: {dll}");
        return $"dotnet \"{dll}\"";
    }

    private static void SkipWithoutPosixShell() =>
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses sh and sleep.");

    private static async Task<(int Exit, string Out, string Err)> RunAsync(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exit = await CliApp.RunAsync(args, stdout, stderr, TestContext.Current.CancellationToken);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    private string WriteSurface(string name, ToolSurface surface)
    {
        var path = _dir.File(name);
        File.WriteAllText(path, surface.ToJson());
        return path;
    }

    [Fact]
    public async Task Snapshot_ThenDiff_AgainstTheSampleServerOverStdio()
    {
        var snapshot = _dir.File("mcp-tools.json");

        var (exit, output, error) = await RunAsync("snapshot", "--stdio", StdioCommand, "--out", snapshot);
        Assert.True(exit == 0, error);
        Assert.Contains("Wrote 3 tool(s)", output, StringComparison.Ordinal);
        Assert.Contains("\"get_forecast\"", File.ReadAllText(snapshot), StringComparison.Ordinal);

        (exit, output, error) = await RunAsync("diff", "--stdio", StdioCommand, "--baseline", snapshot);
        Assert.True(exit == 0, error);
        Assert.Contains("Tool surface unchanged.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lint_SampleServerOverStdio_Passes()
    {
        var (exit, output, error) = await RunAsync("lint", "--stdio", StdioCommand);

        Assert.True(exit == 0, error);
        Assert.Contains("No lint findings.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diff_ExitsOneOnBreakingChange()
    {
        var baseline = WriteSurface("base.json", Surface(Tool("keep"), Tool("gone")));
        var current = WriteSurface("current.json", Surface(Tool("keep")));

        var (exit, output, _) = await RunAsync("diff", "--baseline", baseline, "--from-file", current);

        Assert.Equal(ExitCodes.CheckFailed, exit);
        Assert.Contains("- gone: Tool was removed.", output, StringComparison.Ordinal);
        Assert.Contains("update the snapshot", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diff_NonBreakingPassesUnlessFlagged()
    {
        var baseline = WriteSurface("base.json", Surface(Tool("keep")));
        var current = WriteSurface("current.json", Surface(Tool("keep"), Tool("added")));

        Assert.Equal(ExitCodes.Success, (await RunAsync("diff", "--baseline", baseline, "--from-file", current)).Exit);
        Assert.Equal(ExitCodes.CheckFailed, (await RunAsync("diff", "--baseline", baseline, "--from-file", current, "--fail-on-non-breaking")).Exit);
    }

    [Fact]
    public async Task Diff_MissingBaselineIsAnError()
    {
        var current = WriteSurface("current.json", Surface(Tool("keep")));

        var (exit, _, error) = await RunAsync("diff", "--baseline", _dir.File("nope.json"), "--from-file", current);

        Assert.Equal(ExitCodes.Error, exit);
        Assert.Contains("Baseline snapshot not found", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diff_CorruptBaselineIsAnError()
    {
        var baseline = _dir.File("bad.json");
        File.WriteAllText(baseline, "nope");
        var current = WriteSurface("current.json", Surface(Tool("keep")));

        var (exit, _, error) = await RunAsync("diff", "--baseline", baseline, "--from-file", current);

        Assert.Equal(ExitCodes.Error, exit);
        Assert.Contains("not valid JSON", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lint_FailOnAndRuleOverrides()
    {
        var file = WriteSurface("s.json", Surface(Tool("search", description: "Short.")));

        Assert.Equal(ExitCodes.CheckFailed, (await RunAsync("lint", "--from-file", file)).Exit);
        Assert.Equal(ExitCodes.Success, (await RunAsync("lint", "--from-file", file, "--fail-on", "error")).Exit);
        Assert.Equal(ExitCodes.Success, (await RunAsync("lint", "--from-file", file, "--rule", "MCPP002=none")).Exit);
        Assert.Equal(ExitCodes.Success, (await RunAsync("lint", "--from-file", file, "--min-description-length", "3")).Exit);
        var (exit, output, _) = await RunAsync("lint", "--from-file", file, "--rule", "MCPP002=error", "--fail-on", "error");
        Assert.Equal(ExitCodes.CheckFailed, exit);
        Assert.Contains("error MCPP002 search", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MCPP999=none")]
    [InlineData("MCPP002")]
    [InlineData("MCPP002=loud")]
    public async Task Lint_InvalidRuleIsAnError(string rule)
    {
        var file = WriteSurface("s.json", Surface(Tool("search")));

        var (exit, _, error) = await RunAsync("lint", "--from-file", file, "--rule", rule);

        Assert.Equal(ExitCodes.Error, exit);
        Assert.Contains("Invalid --rule", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new[] { "lint" }, "Pass exactly one of")]
    [InlineData(new[] { "lint", "--stdio", "x", "--http", "http://localhost:1/mcp" }, "Pass exactly one of")]
    [InlineData(new[] { "snapshot", "--from-file", "x.json" }, "--from-file")] // System.CommandLine localizes the message itself
    [InlineData(new[] { "lint", "--http", "ftp://example.com" }, "absolute http:// or https:// URL")]
    [InlineData(new[] { "lint", "--fail-on", "loud", "--stdio", "x" }, "loud")]
    [InlineData(new[] { "frobnicate" }, "frobnicate")]
    public async Task UsageErrors_ExitTwo(string[] args, string expected)
    {
        var (exit, _, error) = await RunAsync(args);

        Assert.Equal(ExitCodes.Error, exit);
        Assert.Contains(expected, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stdio_UnknownExecutableIsAnError()
    {
        var (exit, _, error) = await RunAsync("lint", "--stdio", "definitely-not-a-real-command-mcpprobe", "--timeout", "20");

        Assert.Equal(ExitCodes.Error, exit);
        Assert.StartsWith("error: Failed to connect transport.", error, StringComparison.Ordinal);

        // The inner cause is the useful part; its wording comes from the OS, so only check that there is one.
        var firstLine = error.Split('\n')[0];
        Assert.True(firstLine.Length > "error: Failed to connect transport.".Length + 5, firstLine);
    }

    [Fact]
    public async Task Stdio_ServerThatExitsShowsItsStderr()
    {
        SkipWithoutPosixShell();
        var (exit, _, error) = await RunAsync("lint", "--stdio", "sh -c \"echo boom-from-server >&2; exit 3\"", "--timeout", "20");

        Assert.Equal(ExitCodes.Error, exit);
        Assert.Contains("boom-from-server", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stdio_ServerOutputCannotInjectTerminalEscapes()
    {
        SkipWithoutPosixShell();
        var (exit, _, error) = await RunAsync("lint", "--stdio", "sh -c \"printf '\\033[2Jwiped' >&2; exit 1\"", "--timeout", "20");

        Assert.Equal(ExitCodes.Error, exit);
        Assert.Contains("\\u001B[2Jwiped", error, StringComparison.Ordinal);
        Assert.DoesNotContain('\u001b', error);
    }

    [Fact]
    public async Task Stdio_TimeoutIsReported()
    {
        SkipWithoutPosixShell();
        var (exit, _, error) = await RunAsync("lint", "--stdio", "sleep 30", "--timeout", "1");

        Assert.Equal(ExitCodes.Error, exit);
        Assert.Contains("didn't respond within 1 seconds", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stdio_PassesEnvironmentAndWorkingDirectory()
    {
        SkipWithoutPosixShell();
        var (exit, _, error) = await RunAsync("lint", "--stdio", "sh -c \"echo \\\"$MCPPROBE_TEST_VAR in $(pwd)\\\" >&2; exit 1\"",
            "--env", "MCPPROBE_TEST_VAR=hello-env", "--cwd", _dir.Path, "--timeout", "20");

        Assert.Equal(ExitCodes.Error, exit);
        // Compare the folder name only: on macOS the temp folder is reached through a symlink.
        Assert.Contains($"hello-env in /", error, StringComparison.Ordinal);
        Assert.Contains(Path.GetFileName(_dir.Path), error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_SnapshotAndHeaders()
    {
        var (app, baseUrl) = await StartHttpServerAsync();
        await using var server = app;
        var url = baseUrl + "/mcp";
        var snapshot = _dir.File("http.json");

        var (exit, _, error) = await RunAsync("snapshot", "--http", url, "--out", snapshot);
        Assert.Equal(ExitCodes.Error, exit);
        Assert.Contains("401", error, StringComparison.Ordinal);

        (exit, _, error) = await RunAsync("snapshot", "--http", url, "--header", "X-Api-Key: secret", "--out", snapshot);
        Assert.True(exit == 0, error);
        Assert.Equal(3, ToolSurface.Parse(File.ReadAllText(snapshot)).Tools.Count);

        (exit, _, error) = await RunAsync("diff", "--http", url, "--header", "X-Api-Key: secret", "--baseline", snapshot);
        Assert.True(exit == 0, error);
    }

    [Fact]
    public async Task Http_UnreachableServerIsAnError()
    {
        var (exit, _, error) = await RunAsync("lint", "--http", "http://127.0.0.1:1/mcp", "--timeout", "20");

        Assert.Equal(ExitCodes.Error, exit);
        Assert.StartsWith("error:", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Help_ExitsZero()
    {
        var (exit, output, _) = await RunAsync("--help");

        Assert.Equal(0, exit);
        Assert.Contains("snapshot", output, StringComparison.Ordinal);
        Assert.Contains("diff", output, StringComparison.Ordinal);
        Assert.Contains("lint", output, StringComparison.Ordinal);
    }

    private static async Task<(WebApplication App, string BaseUrl)> StartHttpServerAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddMcpServer().WithHttpTransport().WithWeatherFeatures();

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers["X-Api-Key"] != "secret")
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context);
        });
        app.MapMcp("/mcp");
        await app.StartAsync(TestContext.Current.CancellationToken);

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
        return (app, addresses.Addresses.Single());
    }
}
