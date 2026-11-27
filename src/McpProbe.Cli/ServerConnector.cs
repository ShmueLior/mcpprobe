using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpProbe.Cli;

/// <summary>How to reach the server under test.</summary>
internal sealed record ServerTarget(
    string? StdioCommand,
    Uri? HttpEndpoint,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string?> Environment,
    string? WorkingDirectory);

internal static class ServerConnector
{
    public static async Task<McpClient> ConnectAsync(ServerTarget target, StderrBuffer serverStderr, CancellationToken cancellationToken)
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
            });
        }
        else if (target.HttpEndpoint is not null)
        {
            transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Name = "mcpprobe-target",
                Endpoint = target.HttpEndpoint,
                AdditionalHeaders = target.Headers.Count == 0 ? null : new Dictionary<string, string>(target.Headers),
            });
        }
        else
        {
            throw new ArgumentException("Pass --stdio <command> or --http <url>.");
        }

        var clientOptions = new McpClientOptions
        {
            ClientInfo = new Implementation { Name = "mcpprobe", Version = typeof(ServerConnector).Assembly.GetName().Version?.ToString(3) ?? "0.0.0" },
        };

        return await McpClient.CreateAsync(transport, clientOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Keeps the last lines a stdio server wrote to stderr, to show when it fails.</summary>
internal sealed class StderrBuffer
{
    private const int MaxLines = 20;
    private readonly Queue<string> _lines = new();

    public void Add(string line)
    {
        lock (_lines)
        {
            _lines.Enqueue(line);
            while (_lines.Count > MaxLines)
            {
                _lines.Dequeue();
            }
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
