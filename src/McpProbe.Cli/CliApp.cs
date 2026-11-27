using System.CommandLine;
using System.Globalization;
using McpProbe.Internal;
using McpProbe.Linting;
using McpProbe.Snapshots;

namespace McpProbe.Cli;

/// <summary>The mcpprobe command line. Kept separate from Program so tests can run it in-process.</summary>
internal static class CliApp
{
    private static readonly string[] s_severityNames = ["none", "info", "warning", "error"];

    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken = default)
    {
        var root = BuildRootCommand(stdout, stderr);
        var parse = root.Parse(args);
        if (parse.Errors.Count > 0)
        {
            foreach (var error in parse.Errors)
            {
                await stderr.WriteLineAsync(error.Message).ConfigureAwait(false);
            }

            await stderr.WriteLineAsync("Run 'mcpprobe --help' for usage.").ConfigureAwait(false);
            return ExitCodes.Error;
        }

        var invocation = new InvocationConfiguration { Output = stdout, Error = stderr };
        return await parse.InvokeAsync(invocation, cancellationToken).ConfigureAwait(false);
    }

    private static RootCommand BuildRootCommand(TextWriter stdout, TextWriter stderr)
    {
        var root = new RootCommand("Check an MCP server's tool surface: snapshot it, diff it against a committed snapshot, and lint it.")
        {
            BuildSnapshotCommand(stdout, stderr),
            BuildDiffCommand(stdout, stderr),
            BuildLintCommand(stdout, stderr),
        };
        return root;
    }

    private static Command BuildSnapshotCommand(TextWriter stdout, TextWriter stderr)
    {
        var server = new ServerOptions();
        var output = new Option<string>("--out", "-o")
        {
            Description = "Where to write the snapshot.",
            DefaultValueFactory = _ => ToolSnapshot.DefaultFileName,
        };

        var command = new Command("snapshot", "Connect to a server and write its tool surface to a snapshot file.");
        server.AddTo(command);
        command.Options.Add(output);
        command.SetAction((parse, ct) => RunGuardedAsync(stderr, async () =>
        {
            var surface = await server.CaptureAsync(parse, stderr, ct).ConfigureAwait(false);
            var path = Path.GetFullPath(parse.GetValue(output)!);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(path, surface.ToJson(), ct).ConfigureAwait(false);
            await stdout.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"Wrote {surface.Tools.Count} tool(s) to {path}")).ConfigureAwait(false);
            return ExitCodes.Success;
        }));
        return command;
    }

    private static Command BuildDiffCommand(TextWriter stdout, TextWriter stderr)
    {
        var server = new ServerOptions(allowFile: true);
        var baseline = new Option<string>("--baseline", "-b")
        {
            Description = "The committed snapshot to compare against.",
            DefaultValueFactory = _ => ToolSnapshot.DefaultFileName,
        };
        var failOnNonBreaking = new Option<bool>("--fail-on-non-breaking")
        {
            Description = "Also fail (exit 1) on non-breaking changes, so the snapshot is always kept up to date.",
        };

        var command = new Command("diff", "Compare a server's tool surface with a snapshot. Exits 1 on breaking changes.");
        server.AddTo(command);
        command.Options.Add(baseline);
        command.Options.Add(failOnNonBreaking);
        command.SetAction((parse, ct) => RunGuardedAsync(stderr, async () =>
        {
            var baselinePath = Path.GetFullPath(parse.GetValue(baseline)!);
            if (!File.Exists(baselinePath))
            {
                await stderr.WriteLineAsync($"Baseline snapshot not found: {baselinePath}. Create it with 'mcpprobe snapshot'.").ConfigureAwait(false);
                return ExitCodes.Error;
            }

            var expected = ToolSurface.Parse(await File.ReadAllTextAsync(baselinePath, ct).ConfigureAwait(false));
            var current = await server.CaptureAsync(parse, stderr, ct).ConfigureAwait(false);
            var diff = ToolSurfaceComparer.Compare(expected, current);
            await stdout.WriteLineAsync(diff.ToReport()).ConfigureAwait(false);

            if (diff.HasBreakingChanges || (diff.HasChanges && parse.GetValue(failOnNonBreaking)))
            {
                await stdout.WriteLineAsync("\nIf the change is intended, update the snapshot with 'mcpprobe snapshot' and commit it.").ConfigureAwait(false);
                return ExitCodes.CheckFailed;
            }

            return ExitCodes.Success;
        }));
        return command;
    }

    private static Command BuildLintCommand(TextWriter stdout, TextWriter stderr)
    {
        var server = new ServerOptions(allowFile: true);
        var failOn = new Option<string>("--fail-on")
        {
            Description = "Lowest severity that makes the command exit 1: error, warning, info or none.",
            DefaultValueFactory = _ => "warning",
        };
        failOn.AcceptOnlyFromAmong(s_severityNames);
        var minLength = new Option<int>("--min-description-length")
        {
            Description = "Minimum tool description length for MCPP002.",
            DefaultValueFactory = _ => new LintOptions().MinToolDescriptionLength,
        };
        var rules = new Option<string[]>("--rule")
        {
            Description = "Override a rule's severity, for example --rule MCPP007=none or --rule MCPP003=error. Repeatable.",
            AllowMultipleArgumentsPerToken = false,
        };

        var command = new Command("lint", "Lint tool names, descriptions and parameters. Exits 1 on findings at or above --fail-on.");
        server.AddTo(command);
        command.Options.Add(failOn);
        command.Options.Add(minLength);
        command.Options.Add(rules);
        command.SetAction((parse, ct) => RunGuardedAsync(stderr, async () =>
        {
            var options = new LintOptions
            {
                FailOn = ParseSeverity(parse.GetValue(failOn)!),
                MinToolDescriptionLength = parse.GetValue(minLength),
            };

            foreach (var rule in parse.GetValue(rules) ?? [])
            {
                var parts = rule.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || !LintRules.All.Any(r => string.Equals(r.Id, parts[0], StringComparison.OrdinalIgnoreCase)) ||
                    !s_severityNames.Contains(parts[1], StringComparer.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"Invalid --rule '{rule}'. Use RULE=severity, for example MCPP007=none. Rules: {string.Join(", ", LintRules.All.Select(r => r.Id))}.");
                }

                options.Severities[parts[0]] = ParseSeverity(parts[1]);
            }

            var surface = await server.CaptureAsync(parse, stderr, ct).ConfigureAwait(false);
            var report = ToolLinter.Lint(surface, options);
            await stdout.WriteLineAsync(report.ToReport()).ConfigureAwait(false);
            return report.Failed ? ExitCodes.CheckFailed : ExitCodes.Success;
        }));
        return command;
    }

    private static LintSeverity ParseSeverity(string value) => value.ToUpperInvariant() switch
    {
        "NONE" => LintSeverity.None,
        "INFO" => LintSeverity.Info,
        "WARNING" => LintSeverity.Warning,
        "ERROR" => LintSeverity.Error,
        _ => throw new ArgumentException($"Unknown severity '{value}'."),
    };

    private static async Task<int> RunGuardedAsync(TextWriter stderr, Func<Task<int>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or IOException or UnauthorizedAccessException or HttpRequestException or TimeoutException or ModelContextProtocol.McpException)
        {
            await stderr.WriteLineAsync($"error: {PrintableLines(DescribeWithCauses(ex))}").ConfigureAwait(false);
            return ExitCodes.Error;
        }
        catch (OperationCanceledException)
        {
            await stderr.WriteLineAsync("error: timed out or canceled.").ConfigureAwait(false);
            return ExitCodes.Error;
        }
    }

    /// <summary>
    /// Wrapping exceptions such as "Failed to connect transport." hide the useful part
    /// (for example "No such file or directory"), so append the inner messages.
    /// </summary>
    private static string DescribeWithCauses(Exception ex)
    {
        var messages = new List<string> { ex.Message };
        for (var inner = ex.InnerException; inner is not null && messages.Count < 4; inner = inner.InnerException)
        {
            if (!messages.Any(m => m.Contains(inner.Message, StringComparison.Ordinal)))
            {
                messages.Add(inner.Message);
            }
        }

        return string.Join(" ", messages.Select(m => m.TrimEnd('.') + "."));
    }

    /// <summary>Error messages can carry server text; keep the line breaks, escape everything else.</summary>
    internal static string PrintableLines(string message) =>
        string.Join('\n', message.ReplaceLineEndings("\n").Split('\n').Select(line => Printable.Text(line, 1000, escapeBackslash: false)));
}
