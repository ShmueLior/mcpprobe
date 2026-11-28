using System.Runtime.CompilerServices;
using ModelContextProtocol.Client;

namespace McpProbe.Snapshots;

/// <summary>
/// Checks a server's tool surface against a JSON snapshot file committed next to your tests.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Missing snapshot: it is written and the check fails once, so you review and commit it. On CI it only fails.</item>
/// <item>Breaking change: the check fails with a diff, and the current surface is written to <c>&lt;name&gt;.received.json</c>.</item>
/// <item>Non-breaking change: the check passes (unless <see cref="SnapshotOptions.FailOnNonBreakingChanges"/>) and the diff is reported.</item>
/// <item>Accepting changes: rerun with <c>MCPPROBE_UPDATE_SNAPSHOTS=1</c>.</item>
/// </list>
/// </remarks>
public static class ToolSnapshot
{
    /// <summary>The default snapshot file name.</summary>
    public const string DefaultFileName = "mcp-tools.json";

    /// <summary>Captures the client's tool surface and checks it against a snapshot file.</summary>
    /// <param name="client">A connected client.</param>
    /// <param name="snapshotPath">The snapshot path. Relative paths resolve against <see cref="SnapshotOptions.Directory"/> or the calling source file's directory.</param>
    /// <param name="options">Snapshot options.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <param name="callerFilePath">Filled in by the compiler.</param>
    /// <returns>The diff between the snapshot and the current surface.</returns>
    /// <exception cref="McpProbeSnapshotException">The check failed.</exception>
    public static async Task<ToolSurfaceDiff> VerifyAsync(
        McpClient client,
        string snapshotPath = DefaultFileName,
        SnapshotOptions? options = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string callerFilePath = "")
    {
        ArgumentNullException.ThrowIfNull(client);
        var surface = await ToolSurface.CaptureAsync(client, cancellationToken).ConfigureAwait(false);
        return VerifyResolved(surface, ResolvePath(snapshotPath, options, callerFilePath), options ?? new SnapshotOptions());
    }

    /// <summary>Captures the client's tool surface and checks it against the default snapshot file.</summary>
    /// <param name="client">A connected client.</param>
    /// <param name="snapshotOptions">Snapshot options.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <param name="callerFilePath">Filled in by the compiler.</param>
    /// <returns>The diff between the snapshot and the current surface.</returns>
    /// <exception cref="McpProbeSnapshotException">The check failed.</exception>
    public static Task<ToolSurfaceDiff> VerifyAsync(
        McpClient client,
        SnapshotOptions snapshotOptions,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string callerFilePath = "")
    {
        ArgumentNullException.ThrowIfNull(snapshotOptions);
        return VerifyAsync(client, DefaultFileName, snapshotOptions, cancellationToken, callerFilePath);
    }

    /// <summary>Checks a captured surface against a snapshot file.</summary>
    /// <param name="current">The current surface.</param>
    /// <param name="snapshotPath">The snapshot path. Relative paths resolve against <see cref="SnapshotOptions.Directory"/> or the calling source file's directory.</param>
    /// <param name="options">Snapshot options.</param>
    /// <param name="callerFilePath">Filled in by the compiler.</param>
    /// <returns>The diff between the snapshot and the current surface.</returns>
    /// <exception cref="McpProbeSnapshotException">The check failed.</exception>
    public static ToolSurfaceDiff Verify(
        ToolSurface current,
        string snapshotPath = DefaultFileName,
        SnapshotOptions? options = null,
        [CallerFilePath] string callerFilePath = "")
    {
        ArgumentNullException.ThrowIfNull(current);
        return VerifyResolved(current, ResolvePath(snapshotPath, options, callerFilePath), options ?? new SnapshotOptions());
    }

    /// <summary>Checks a captured surface against the default snapshot file.</summary>
    /// <param name="current">The current surface.</param>
    /// <param name="snapshotOptions">Snapshot options.</param>
    /// <param name="callerFilePath">Filled in by the compiler.</param>
    /// <returns>The diff between the snapshot and the current surface.</returns>
    /// <exception cref="McpProbeSnapshotException">The check failed.</exception>
    public static ToolSurfaceDiff Verify(
        ToolSurface current,
        SnapshotOptions snapshotOptions,
        [CallerFilePath] string callerFilePath = "")
    {
        ArgumentNullException.ThrowIfNull(snapshotOptions);
        return Verify(current, DefaultFileName, snapshotOptions, callerFilePath);
    }

    /// <summary>Gets the path of the file written next to a snapshot when the check fails.</summary>
    /// <param name="snapshotPath">The snapshot path.</param>
    /// <returns>The <c>.received.json</c> path.</returns>
    public static string GetReceivedPath(string snapshotPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(snapshotPath);
        var directory = Path.GetDirectoryName(snapshotPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(snapshotPath);
        return Path.Combine(directory, name + ".received.json");
    }

    private static ToolSurfaceDiff VerifyResolved(ToolSurface current, string path, SnapshotOptions options)
    {
        var receivedPath = GetReceivedPath(path);
        var currentJson = current.ToJson();

        if (!File.Exists(path))
        {
            if (options.ResolveUpdate() || options.ResolveCreateMissing())
            {
                WriteFile(path, currentJson);
                DeleteIfExists(receivedPath);
                if (options.ResolveUpdate())
                {
                    return ToolSurfaceComparer.Compare(current, current);
                }

                throw new McpProbeSnapshotException(
                    $"No tool snapshot existed, so one was written to:\n  {path}\nReview it, commit it, and run the tests again.",
                    path,
                    diff: null);
            }

            WriteFile(receivedPath, currentJson);
            throw new McpProbeSnapshotException(
                $"Tool snapshot not found:\n  {path}\nCreate it by running the tests locally (or with {SnapshotOptions.UpdateEnvironmentVariable}=1) and commit it. The current surface was written to:\n  {receivedPath}",
                path,
                diff: null);
        }

        ToolSurface baseline;
        try
        {
            baseline = ToolSurface.Parse(ReadFile(path));
        }
        catch (FormatException ex) when (!options.ResolveUpdate())
        {
            throw new McpProbeSnapshotException($"The tool snapshot at {path} could not be read: {ex.Message}", ex);
        }
        catch (FormatException)
        {
            WriteFile(path, currentJson);
            DeleteIfExists(receivedPath);
            return ToolSurfaceComparer.Compare(current, current);
        }

        var diff = ToolSurfaceComparer.Compare(baseline, current);

        if (options.ResolveUpdate())
        {
            if (diff.HasChanges || !string.Equals(ReadFile(path), currentJson, StringComparison.Ordinal))
            {
                WriteFile(path, currentJson);
            }

            DeleteIfExists(receivedPath);
            return diff;
        }

        if (diff.HasBreakingChanges || (diff.HasChanges && options.FailOnNonBreakingChanges))
        {
            WriteFile(receivedPath, currentJson);
            var reason = diff.HasBreakingChanges ? "has breaking changes" : "changed";
            throw new McpProbeSnapshotException(
                $"The tool surface {reason} compared with the snapshot at:\n  {path}\n\n{diff.ToReport()}\n\n" +
                $"The current surface was written to:\n  {receivedPath}\n" +
                $"If the change is intended, accept it by running the tests with {SnapshotOptions.UpdateEnvironmentVariable}=1 and commit the snapshot.",
                path,
                diff);
        }

        DeleteIfExists(receivedPath);
        if (diff.HasChanges)
        {
            options.Log?.Invoke(diff.ToReport() + $"\nUpdate the snapshot with {SnapshotOptions.UpdateEnvironmentVariable}=1 to accept these changes.");
        }

        return diff;
    }

    internal static string ResolvePath(string snapshotPath, SnapshotOptions? options, string callerFilePath, string? searchRoot = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(snapshotPath);
        if (Path.IsPathRooted(snapshotPath))
        {
            return Path.GetFullPath(snapshotPath);
        }

        var baseDirectory = options?.Directory;
        if (baseDirectory is null)
        {
            var callerDirectory = string.IsNullOrEmpty(callerFilePath) ? null : Path.GetDirectoryName(callerFilePath);
            if (callerDirectory is null || !System.IO.Directory.Exists(callerDirectory))
            {
                callerDirectory = FindMappedSourceDirectory(callerFilePath, searchRoot ?? AppContext.BaseDirectory) ??
                    throw new InvalidOperationException(
                        $"Can't find the source directory for the snapshot '{snapshotPath}' (caller path: '{callerFilePath}'). " +
                        "This happens when the test project is built with path mapping (for example ContinuousIntegrationBuild=true) " +
                        "and the sources aren't under a parent folder of the test binaries. " +
                        "Pass an absolute path or set SnapshotOptions.Directory.");
            }

            baseDirectory = callerDirectory;
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, snapshotPath));
    }

    /// <summary>
    /// Deterministic CI builds map source paths to something like <c>/_/tests/MyTests.cs</c>. The sources are
    /// still on disk, usually in a parent folder of the test binaries, so look for the longest tail of the
    /// mapped path under each parent of <paramref name="searchRoot"/>.
    /// </summary>
    private static string? FindMappedSourceDirectory(string callerFilePath, string searchRoot)
    {
        if (string.IsNullOrEmpty(callerFilePath))
        {
            return null;
        }

        var segments = callerFilePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        // Keep at least a folder and the file name, so a bare file name can't match an unrelated file.
        for (var skip = 1; skip < segments.Length - 1; skip++)
        {
            var tail = Path.Combine(segments[skip..]);
            for (var directory = new DirectoryInfo(searchRoot); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, tail);
                if (File.Exists(candidate))
                {
                    return Path.GetDirectoryName(candidate);
                }
            }
        }

        return null;
    }

    private static void WriteFile(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        // Write to a temporary file and rename it over the target, so a test run for another target
        // framework reading the snapshot at the same time never sees a partial file.
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, content);
            WithRetry(() => File.Move(temporary, path, overwrite: true));
        }
        finally
        {
            DeleteIfExists(temporary);
        }
    }

    /// <summary>
    /// Reads with delete sharing, so another test process (one per target framework) can replace the file
    /// while it is being read; Windows would otherwise refuse the replace.
    /// </summary>
    private static string ReadFile(string path)
    {
        string? content = null;
        WithRetry(() =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            content = reader.ReadToEnd();
        });
        return content!;
    }

    /// <summary>Windows briefly locks a file while another process replaces it; retry a few times.</summary>
    private static void WithRetry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && ex is not FileNotFoundException && attempt < 10)
            {
                Thread.Sleep(20 * attempt);
            }
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
