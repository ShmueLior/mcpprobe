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

    [Fact]
    public async Task Lint_NestedIsOptIn()
    {
        var schema = """{"type":"object","properties":{"home":{"type":"object","description":"Home address.","properties":{"zip":{"type":"string"}}}}}""";
        var file = WriteSurface("nested.json", Surface(Tool("search", schema, description: "Searches the catalog for matching items.")));

        Assert.Equal(ExitCodes.Success, (await RunAsync("lint", "--from-file", file)).Exit);
        var (exit, output, _) = await RunAsync("lint", "--from-file", file, "--nested");
        Assert.Equal(ExitCodes.CheckFailed, exit);
        Assert.Contains("MCPP003 search.home.zip", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lint_SampleServerOverStdio_PassesWithStrict()
    {
        var (exit, output, error) = await RunAsync("lint", "--stdio", StdioCommand, "--strict");

        Assert.True(exit == 0, error);
        Assert.Contains("No lint findings.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lint_Strict_IsOptInAndFailsOnlyOnPortabilityErrors()
    {
        var allRules = """{"type":"object","properties":{"city":{"description":"City name.","anyOf":[true,{"type":["boolean","integer"]},{},{"$ref":"https://example.com/schema.json"}]}}}""";
        var warningsOnly = """{"type":"object","properties":{"city":{"description":"City name.","type":["boolean","integer"]}}}""";
        var allFile = WriteSurface("all.json", Surface(Tool("unportable", allRules, "A tool whose schema exercises each portability rule.")));
        var warnFile = WriteSurface("warn.json", Surface(Tool("union", warningsOnly, "A tool whose schema uses a type union.")));

        var off = await RunAsync("lint", "--from-file", allFile);
        Assert.Equal(ExitCodes.Success, off.Exit);
        Assert.Contains("No lint findings.", off.Out, StringComparison.Ordinal);

        var (exit, output, _) = await RunAsync("lint", "--from-file", allFile, "--strict");
        Assert.Equal(ExitCodes.CheckFailed, exit);
        Assert.Contains("error MCPP009", output, StringComparison.Ordinal);
        Assert.Contains("warning MCPP010", output, StringComparison.Ordinal);
        Assert.Contains("`type` is a union array", output, StringComparison.Ordinal);
        Assert.DoesNotContain("...", output, StringComparison.Ordinal);
        Assert.Contains("warning MCPP011", output, StringComparison.Ordinal);
        Assert.Contains("warning MCPP012", output, StringComparison.Ordinal);

        var warnings = await RunAsync("lint", "--from-file", warnFile, "--strict");
        Assert.Equal(ExitCodes.Success, warnings.Exit);
        Assert.Contains("warning MCPP010", warnings.Out, StringComparison.Ordinal);

        var upgraded = await RunAsync("lint", "--from-file", warnFile, "--rule", "MCPP010=error");
        Assert.Equal(ExitCodes.CheckFailed, upgraded.Exit);
        Assert.Contains("error MCPP010", upgraded.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_Lint_WritesHtmlAndLeavesStdoutAndExitCodeUnchanged()
    {
        var file = WriteSurface("tools.json", Surface(Tool("ok")));
        var failing = WriteSurface("failing.json", Surface(Tool("bad", description: "")));
        var report = _dir.File(Path.Combine("nested", "dir", "lint.html"));

        var plain = await RunAsync("lint", "--from-file", file);
        var withReport = await RunAsync("lint", "--from-file", file, "--report", report);

        Assert.Equal(plain, withReport);
        Assert.Contains("banner pass", File.ReadAllText(report), StringComparison.Ordinal);

        var failedPlain = await RunAsync("lint", "--from-file", failing);
        var failed = await RunAsync("lint", "--from-file", failing, "--report", report);
        Assert.Equal(ExitCodes.CheckFailed, failed.Exit);
        Assert.Equal(failedPlain, failed);
        Assert.Contains("banner fail", File.ReadAllText(report), StringComparison.Ordinal);
        Assert.Contains("MCPP001", File.ReadAllText(report), StringComparison.Ordinal);
        Assert.Equal(["lint.html"], Directory.GetFiles(Path.GetDirectoryName(report)!).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Report_Diff_WritesHtmlOnExitZeroAndOne()
    {
        var baseline = WriteSurface("base.json", Surface(Tool("keep"), Tool("gone")));
        var same = WriteSurface("same.json", Surface(Tool("keep"), Tool("gone")));
        var added = WriteSurface("added.json", Surface(Tool("keep"), Tool("gone"), Tool("added")));
        var removed = WriteSurface("removed.json", Surface(Tool("keep")));
        var report = _dir.File("diff.html");

        var plainSame = await RunAsync("diff", "--baseline", baseline, "--from-file", same);
        var sameWithReport = await RunAsync("diff", "--baseline", baseline, "--from-file", same, "--report", report);
        Assert.Equal(ExitCodes.Success, sameWithReport.Exit);
        Assert.Equal(plainSame, sameWithReport);
        Assert.Contains("Tool surface unchanged.", File.ReadAllText(report), StringComparison.Ordinal);

        var plainAdded = await RunAsync("diff", "--baseline", baseline, "--from-file", added);
        var addedWithReport = await RunAsync("diff", "--baseline", baseline, "--from-file", added, "--report", report);
        Assert.Equal(ExitCodes.Success, addedWithReport.Exit);
        Assert.Equal(plainAdded, addedWithReport);
        Assert.Contains("Non-breaking changes only", File.ReadAllText(report), StringComparison.Ordinal);

        var flagged = await RunAsync("diff", "--baseline", baseline, "--from-file", added, "--fail-on-non-breaking", "--report", report);
        Assert.Equal(ExitCodes.CheckFailed, flagged.Exit);
        Assert.Contains("Non-breaking changes only", File.ReadAllText(report), StringComparison.Ordinal);

        var plain = await RunAsync("diff", "--baseline", baseline, "--from-file", removed);
        var breaking = await RunAsync("diff", "--baseline", baseline, "--from-file", removed, "--report", report);
        Assert.Equal(ExitCodes.CheckFailed, breaking.Exit);
        Assert.Equal(plain, breaking);
        Assert.Contains("Breaking changes", File.ReadAllText(report), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_SampleServerOverStdio_WritesBothReports()
    {
        var snapshot = _dir.File("mcp-tools.json");
        var lintReport = _dir.File("lint.html");
        var diffReport = _dir.File("diff.html");
        Assert.Equal(0, (await RunAsync("snapshot", "--stdio", StdioCommand, "--out", snapshot)).Exit);

        Assert.Equal(0, (await RunAsync("lint", "--stdio", StdioCommand, "--report", lintReport)).Exit);
        Assert.Equal(0, (await RunAsync("diff", "--stdio", StdioCommand, "--baseline", snapshot, "--report", diffReport)).Exit);

        Assert.Contains("No lint findings.", File.ReadAllText(lintReport), StringComparison.Ordinal);
        Assert.Contains("Tool surface unchanged.", File.ReadAllText(diffReport), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_UsageAndConnectionErrorsDoNotWriteTheFile()
    {
        var current = WriteSurface("current.json", Surface(Tool("keep")));
        var report = _dir.File("never.html");

        var results = new[]
        {
            await RunAsync("lint", "--from-file", _dir.File("missing.json"), "--report", report),
            await RunAsync("lint", "--from-file", current, "--rule", "MCPP999=none", "--report", report),
            await RunAsync("lint", "--report", report),
            await RunAsync("lint", "--http", "not a url", "--report", report),
            await RunAsync("diff", "--baseline", _dir.File("nope.json"), "--from-file", current, "--report", report),
            await RunAsync("diff", "--baseline", current, "--from-file", _dir.File("missing.json"), "--report", report),
        };

        Assert.All(results, r => Assert.Equal(ExitCodes.Error, r.Exit));
        Assert.False(File.Exists(report));
        Assert.Empty(Directory.GetFiles(_dir.Path, "never*"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Report_BlankPathIsAUsageError(string path)
    {
        var current = WriteSurface("current.json", Surface(Tool("keep")));

        var lint = await RunAsync("lint", "--from-file", current, "--report", path);
        var diff = await RunAsync("diff", "--baseline", current, "--from-file", current, "--report", path);

        Assert.Equal(ExitCodes.Error, lint.Exit);
        Assert.Equal(ExitCodes.Error, diff.Exit);
        Assert.Contains("--report needs a file path", lint.Err + diff.Err, StringComparison.Ordinal);
        Assert.Equal("", lint.Out + diff.Out);
    }

    [Fact]
    public async Task Report_ReplacesAnExistingFile_AndAFailedWriteLeavesNoPartialFile()
    {
        var current = WriteSurface("current.json", Surface(Tool("keep")));
        var report = _dir.File("report.html");
        File.WriteAllText(report, "old");

        Assert.Equal(0, (await RunAsync("lint", "--from-file", current, "--report", report)).Exit);
        Assert.StartsWith("<!DOCTYPE html>", File.ReadAllText(report), StringComparison.Ordinal);

        var directory = _dir.File("a-directory");
        Directory.CreateDirectory(directory);
        var (exit, _, error) = await RunAsync("lint", "--from-file", current, "--report", directory);

        Assert.Equal(ExitCodes.Error, exit);
        Assert.StartsWith("error:", error, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(directory));
        Assert.DoesNotContain(Directory.GetFiles(_dir.Path), f => f.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("MCPP999=none")]
    [InlineData("MCPP008=error")]
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
    [InlineData(new[] { "snapshot", "--stdio", "x", "--transport", "sse" }, "can only be used with --http")]
    [InlineData(new[] { "diff", "--from-file", "x.json", "--transport", "sse" }, "can only be used with --http")]
    [InlineData(new[] { "lint", "--from-file", "x.json", "--transport", "sse" }, "can only be used with --http")]
    [InlineData(new[] { "lint", "--stdio", "x", "--transport", "SSE" }, "can only be used with --http")]
    [InlineData(new[] { "lint", "--http", "http://localhost:1/mcp", "--transport", "ws" }, "must be auto, streamable-http, or sse")]
    [InlineData(new[] { "lint", "--http", "http://localhost:1/mcp", "--transport", "websocket" }, "must be auto, streamable-http, or sse")]
    [InlineData(new[] { "lint", "--http", "http://localhost:1/mcp", "--transport", "" }, "must be auto, streamable-http, or sse")]
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

        // On Windows the SDK starts the command through cmd.exe, so the server's stderr tail
        // ("... is not recognized ...") comes first and the error is about the process exiting.
        var errorLine = Assert.Single(error.Split('\n'), line => line.StartsWith("error: ", StringComparison.Ordinal));
        if (!OperatingSystem.IsWindows())
        {
            // The inner cause is the useful part; its wording comes from the OS, so only check that there is one.
            Assert.StartsWith("error: Failed to connect transport.", errorLine, StringComparison.Ordinal);
            Assert.True(errorLine.Length > "error: Failed to connect transport.".Length + 5, errorLine);
        }
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
    public async Task Stdio_ServerThatExitsSilently_FailsWithoutWaitingLong()
    {
        SkipWithoutPosixShell();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var (exit, _, error) = await RunAsync("lint", "--stdio", "sh -c \"exit 3\"", "--timeout", "20");

        Assert.Equal(ExitCodes.Error, exit);
        Assert.StartsWith("error: ", error, StringComparison.Ordinal);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), watch.Elapsed.ToString());
    }

    [Fact]
    public async Task StderrBuffer_WaitsForLinesThatArriveLate()
    {
        // The SDK delivers stderr on a background thread; under load a line can arrive after the connect error.
        var buffer = new StderrBuffer();
        var late = Task.Run(async () =>
        {
            await Task.Delay(300, TestContext.Current.CancellationToken);
            buffer.Add("late line");
            await Task.Delay(100, TestContext.Current.CancellationToken);
            buffer.Add("last line");
        }, TestContext.Current.CancellationToken);

        await buffer.WaitForLinesAsync(TestContext.Current.CancellationToken);
        await late;

        Assert.Equal("late line\nlast line", buffer.ToString());
    }

    [Fact]
    public async Task StderrBuffer_StopsWaitingWhenNothingArrives_OrWhenLinesKeepComing()
    {
        var empty = new StderrBuffer();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await empty.WaitForLinesAsync(TestContext.Current.CancellationToken);
        Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(1.9), TimeSpan.FromSeconds(10));

        var chatty = new StderrBuffer();
        using var stop = new CancellationTokenSource();
        var writer = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                chatty.Add("log");
                await Task.Delay(50, CancellationToken.None);
            }
        }, CancellationToken.None);
        watch.Restart();
        await chatty.WaitForLinesAsync(TestContext.Current.CancellationToken);
        await stop.CancelAsync();
        await writer;
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), watch.Elapsed.ToString());
    }

    [Theory]
    [InlineData("sleep 30")]
    [InlineData("sh -c \"trap '' TERM; sleep 30\"")]
    public async Task Stdio_TimeoutIsReported(string command)
    {
        SkipWithoutPosixShell();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var (exit, _, error) = await RunAsync("lint", "--stdio", command, "--timeout", "2");

        Assert.Equal(ExitCodes.Error, exit);
        Assert.Equal("error: The server didn't respond within 2 seconds.", error.TrimEnd());
        // The SDK waits 5 seconds for a graceful exit by default; the limit is only loose enough for a busy CI machine.
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4), watch.Elapsed.ToString());
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
    public async Task Http_TransportAutoAndStreamableHttp_WorkAgainstMcpEndpoint()
    {
        var (app, baseUrl) = await StartHttpServerAsync();
        await using var server = app;
        var url = baseUrl + "/mcp";
        var snapshot = _dir.File("transport.json");

        var (exit, _, error) = await RunAsync("snapshot", "--http", url, "--header", "X-Api-Key: secret", "--out", snapshot);
        Assert.True(exit == 0, error);
        Assert.Equal(3, ToolSurface.Parse(File.ReadAllText(snapshot)).Tools.Count);

        (exit, _, error) = await RunAsync("lint", "--http", url, "--transport", "auto", "--header", "X-Api-Key: secret");
        Assert.True(exit == 0, error);

        (exit, _, error) = await RunAsync("lint", "--http", url, "--transport", "streamable-http", "--header", "X-Api-Key: secret");
        Assert.True(exit == 0, error);

        (exit, _, error) = await RunAsync("diff", "--http", url, "--transport", "Auto", "--header", "X-Api-Key: secret", "--baseline", snapshot);
        Assert.True(exit == 0, error);
    }

    [Fact]
    public async Task Http_TransportSseAgainstStreamableHttpEndpoint_IsAnError()
    {
        var (app, baseUrl) = await StartHttpServerAsync();
        await using var server = app;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var (exit, _, error) = await RunAsync(
            "lint", "--http", baseUrl + "/mcp", "--transport", "sse", "--header", "X-Api-Key: secret", "--timeout", "10");

        Assert.Equal(ExitCodes.Error, exit);
        Assert.StartsWith("error: The server did not answer as a legacy SSE endpoint at this URL (HTTP ", error, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionMode", error, StringComparison.Ordinal);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), watch.Elapsed.ToString());
    }

    [Fact]
    public async Task Http_TransportStreamableHttpAgainstLegacySseEndpoint_IsAnError()
    {
        var (app, baseUrl) = await StartHttpServerAsync(enableLegacySse: true);
        await using var server = app;

        var (exit, _, error) = await RunAsync(
            "lint", "--http", baseUrl + "/mcp/sse", "--transport", "streamable-http", "--header", "X-Api-Key: secret", "--timeout", "10");

        Assert.Equal(ExitCodes.Error, exit);
        Assert.StartsWith("error: The server did not answer as Streamable HTTP at this URL (HTTP ", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sse", "legacy SSE endpoint")]
    [InlineData("streamable-http", "as Streamable HTTP")]
    public async Task Http_PinnedTransportAgainstHtmlPage_GetsTheTransportHint(string transport, string expected)
    {
        await using var stub = await StartStubAsync(context => Respond(context, 405, "<html><body><h1>405</h1><p>Not here</p></body></html>"));

        var (exit, _, error) = await RunAsync("lint", "--http", stub.Url, "--transport", transport, "--timeout", "10");

        Assert.Equal(ExitCodes.Error, exit);
        Assert.Contains(expected, error, StringComparison.Ordinal);
        Assert.Contains("(HTTP 405)", error, StringComparison.Ordinal);
        Assert.DoesNotContain("<", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_AutoTransportAgainstHtmlPage_ShowsAShortPreviewWithoutTags()
    {
        var body = "<!DOCTYPE html><html><head><title>Nope</title></head><body><h1>Not &amp; here</h1>\n<p>" + new string('x', 500) + "</p></body></html>";
        await using var stub = await StartStubAsync(context => Respond(context, 405, body));

        var (exit, _, error) = await RunAsync("lint", "--http", stub.Url, "--timeout", "10");

        Assert.Equal(ExitCodes.Error, exit);
        Assert.StartsWith($"error: HTTP 405 from {stub.Url}: \"Nope Not & here xxx", error, StringComparison.Ordinal);
        Assert.EndsWith("...\".", error.TrimEnd(), StringComparison.Ordinal);
        Assert.Single(error.TrimEnd().Split('\n'));
        Assert.DoesNotContain("<", error, StringComparison.Ordinal);
        Assert.True(error.Length < 400, $"{error.Length}");
    }

    [Fact]
    public async Task Http_ServerError_HasNoTransportHint()
    {
        await using var stub = await StartStubAsync(context => Respond(context, 500, "boom"));

        var (exit, _, error) = await RunAsync("lint", "--http", stub.Url, "--transport", "sse", "--timeout", "10");

        Assert.Equal(ExitCodes.Error, exit);
        Assert.Equal($"error: HTTP 500 from {stub.Url}: \"boom\".", error.TrimEnd());
    }

    [Fact]
    public async Task Http_PreviewEndingWithAPeriod_DoesNotGetASecondPeriodAfterTheQuote()
    {
        await using var stub = await StartStubAsync(context => Respond(context, 404, "Profile 'nonexistent' not found."));

        var (exit, _, error) = await RunAsync("lint", "--http", stub.Url, "--timeout", "10");

        Assert.Equal(ExitCodes.Error, exit);
        Assert.Equal($"error: HTTP 404 from {stub.Url}: \"Profile 'nonexistent' not found\".", error.TrimEnd());
    }

    [Fact]
    public async Task Http_HostileBody_IsEscaped()
    {
        await using var stub = await StartStubAsync(context => Respond(context, 502, "bad \u001b[31mred\u001b[0m \u202E gateway"));

        var (exit, _, error) = await RunAsync("lint", "--http", stub.Url, "--timeout", "10");

        Assert.Equal(ExitCodes.Error, exit);
        Assert.DoesNotContain('\u001b', error);
        Assert.DoesNotContain('\u202E', error);
        Assert.Contains("\\u001B[31mred", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_Unauthorized_SaysWhatTheServerWantsAndWhatToPass()
    {
        await using var stub = await StartStubAsync(context =>
        {
            context.Response.Headers.WWWAuthenticate = "Bearer resource_metadata=\"http://example.test/.well-known/x\"";
            return Respond(context, 401, "");
        });

        var (exit, _, error) = await RunAsync("lint", "--http", stub.Url + "?token=secret", "--timeout", "10");

        Assert.Equal(ExitCodes.Error, exit);
        Assert.Equal(
            $"error: 401 Unauthorized from {stub.Url}. The server wants authentication (WWW-Authenticate: Bearer resource_metadata=\"http://example.test/.well-known/x\"). Pass --header 'Authorization: Bearer <token>'.",
            error.TrimEnd());
    }

    [Fact]
    public async Task Http_UnauthorizedWithAuthorizationHeader_SaysTheCredentialsWereRejected()
    {
        await using var stub = await StartStubAsync(context => Respond(context, 401, ""));

        var (exit, _, error) = await RunAsync("lint", "--http", stub.Url, "--header", "authorization: Bearer nope", "--timeout", "10");

        Assert.Equal(ExitCodes.Error, exit);
        Assert.Equal($"error: 401 Unauthorized from {stub.Url}. The credentials in --header were rejected.", error.TrimEnd());
        Assert.DoesNotContain("nope", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_TransportSse_WorksAgainstLegacySseEndpoint()
    {
        var (app, baseUrl) = await StartHttpServerAsync(enableLegacySse: true);
        await using var server = app;

        // MapMcp("/mcp") with EnableLegacySse maps legacy endpoints under the prefix, at /mcp/sse and /mcp/message.
        var (exit, output, error) = await RunAsync(
            "lint", "--http", baseUrl + "/mcp/sse", "--transport", "sse", "--header", "X-Api-Key: secret");
        Assert.True(exit == 0, error);
        Assert.Contains("No lint findings.", output, StringComparison.Ordinal);

        (exit, _, error) = await RunAsync(
            "lint", "--http", baseUrl + "/mcp", "--transport", "streamable-http", "--header", "X-Api-Key: secret");
        Assert.True(exit == 0, error);
    }

    [Fact]
    public async Task Help_TransportDescribesLegacySse()
    {
        var (exit, output, _) = await RunAsync("lint", "--help");

        Assert.Equal(0, exit);
        Assert.Contains("--transport", output, StringComparison.Ordinal);
        Assert.Contains("legacy SSE", output, StringComparison.Ordinal);
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

    private static Task Respond(HttpContext context, int status, string body)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsync(body);
    }

    /// <summary>A plain HTTP server that answers every request with <paramref name="handler"/>, no MCP.</summary>
    private static async Task<Stub> StartStubAsync(Func<HttpContext, Task> handler)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.MapFallback(new RequestDelegate(handler));
        await app.StartAsync(TestContext.Current.CancellationToken);
        return new Stub(app, app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/mcp");
    }

    private sealed class Stub(WebApplication app, string url) : IAsyncDisposable
    {
        public string Url { get; } = url;

        public ValueTask DisposeAsync() => app.DisposeAsync();
    }

    private static async Task<(WebApplication App, string BaseUrl)> StartHttpServerAsync(bool enableLegacySse = false)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var mcp = builder.Services.AddMcpServer();
        if (enableLegacySse)
        {
#pragma warning disable MCP9004 // Needed to exercise the legacy SSE client path.
            mcp.WithHttpTransport(o =>
            {
                o.SessionMode = ModelContextProtocol.AspNetCore.HttpServerSessionMode.Stateful;
                o.EnableLegacySse = true;
            });
#pragma warning restore MCP9004
        }
        else
        {
            mcp.WithHttpTransport();
        }

        mcp.WithWeatherFeatures();

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
