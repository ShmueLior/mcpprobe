using System.Diagnostics;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpProbe.Cli;

/// <summary>How to reach the server under test.</summary>
internal sealed record ServerTarget(
    string? StdioCommand,
    Uri? HttpEndpoint,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string?> Environment,
    string? WorkingDirectory,
    HttpTransportMode TransportMode,
    TimeSpan Timeout);

internal static class ServerConnector
{
    /// <summary>
    /// How long a stdio server gets to exit by itself after its stdin is closed, before its process tree is killed.
    /// The SDK default is 5 seconds, which would add that much to a --timeout.
    /// </summary>
    private static readonly TimeSpan s_shutdownTimeout = TimeSpan.FromMilliseconds(500);

    public static async Task<McpClient> ConnectAsync(ServerTarget target, StderrBuffer serverStderr, ChallengeCapture challenge, CancellationToken cancellationToken)
    {
        IClientTransport transport;
        if (target.StdioCommand is not null)
        {
            var parts = CommandLineSplitter.Split(target.StdioCommand);
            if (parts.Count == 0)
            {
                throw new ArgumentException("--stdio needs a command, for example --stdio \"dotnet run --project src/MyServer\".");
            }

            transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "mcpprobe-target",
                Command = parts[0],
                Arguments = parts.Skip(1).ToList(),
                WorkingDirectory = target.WorkingDirectory,
                EnvironmentVariables = target.Environment.Count == 0 ? null : new Dictionary<string, string?>(target.Environment),
                StandardErrorLines = serverStderr.Add,
                ShutdownTimeout = s_shutdownTimeout,
            });
        }
        else if (target.HttpEndpoint is not null)
        {
            // --timeout is the only time limit, so the HttpClient has none of its own.
            var http = new HttpClient(challenge, disposeHandler: true)
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
            transport = new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Name = "mcpprobe-target",
                    Endpoint = target.HttpEndpoint,
                    AdditionalHeaders = target.Headers.Count == 0 ? null : new Dictionary<string, string>(target.Headers),
                    TransportMode = target.TransportMode,
                    ConnectionTimeout = target.Timeout,
                },
                http,
                loggerFactory: null,
                ownsHttpClient: true);
        }
        else
        {
            throw new ArgumentException("Pass --stdio <command> or --http <url>.");
        }

        var clientOptions = new McpClientOptions
        {
            ClientInfo = new Implementation { Name = "mcpprobe", Version = typeof(ServerConnector).Assembly.GetName().Version?.ToString(3) ?? "0.0.0" },
        };

        if (target.StdioCommand is not null)
        {
            transport = new StartTracking(transport, serverStderr);
        }

        return await McpClient.CreateAsync(transport, clientOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records in the stderr buffer that the server process was started.</summary>
    private sealed class StartTracking(IClientTransport inner, StderrBuffer serverStderr) : IClientTransport
    {
        public string Name => inner.Name;

        public async Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
        {
            var transport = await inner.ConnectAsync(cancellationToken).ConfigureAwait(false);
            serverStderr.ProcessStarted = true;
            return transport;
        }
    }
}

/// <summary>Keeps the last lines a stdio server wrote to stderr, to show when it fails.</summary>
internal sealed class StderrBuffer
{
    private const int MaxLines = 20;
    private static readonly TimeSpan s_firstLineWait = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan s_quietPeriod = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan s_maxWait = TimeSpan.FromSeconds(3);
    private readonly Queue<string> _lines = new();
    private long _lastLineTimestamp;

    /// <summary>Gets or sets whether the stdio server process was started (so it may still write to stderr).</summary>
    public bool ProcessStarted { get; set; }

    public void Add(string line)
    {
        lock (_lines)
        {
            _lastLineTimestamp = Stopwatch.GetTimestamp();
            _lines.Enqueue(line);
            while (_lines.Count > MaxLines)
            {
                _lines.Dequeue();
            }
        }
    }

    /// <summary>
    /// The SDK delivers stderr lines on a background thread, so after a failure they can still be on the way.
    /// Waits up to 2 seconds for the first line, then until no new line arrives for 200 ms, and never more than 3 seconds
    /// (a server that is still running may keep logging).
    /// </summary>
    public async Task WaitForLinesAsync(CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();
        while (true)
        {
            long last;
            lock (_lines)
            {
                last = _lastLineTimestamp;
            }

            var waited = Stopwatch.GetElapsedTime(start);
            if (waited >= s_maxWait || (last == 0 ? waited >= s_firstLineWait : Stopwatch.GetElapsedTime(last) >= s_quietPeriod))
            {
                return;
            }

            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
    }

    public override string ToString()
    {
        lock (_lines)
        {
            return string.Join("\n", _lines);
        }
    }
}
