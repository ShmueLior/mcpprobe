using System.CommandLine;
using McpProbe.Snapshots;
using ModelContextProtocol.Client;

namespace McpProbe.Cli;

/// <summary>The options every command uses to find the tool surface: a live server, or a snapshot file.</summary>
internal sealed class ServerOptions
{
    private readonly Option<string> _stdio = new("--stdio")
    {
        Description = "Start the server with this command line and talk to it over stdio, for example \"dotnet run --project src/MyServer\".",
    };

    private readonly Option<string> _http = new("--http")
    {
        Description = "Connect to a server over HTTP (Streamable HTTP, falling back to legacy SSE), for example http://localhost:5000/mcp.",
    };

    private readonly Option<string> _transport = new("--transport")
    {
        Description = "HTTP transport to use: auto (try Streamable HTTP, then fall back to legacy SSE), streamable-http, or sse (the legacy SSE transport, not the event stream inside Streamable HTTP). Only with --http.",
        DefaultValueFactory = _ => "auto",
    };

    private readonly Option<string[]> _headers = new("--header")
    {
        Description = "HTTP header as 'Name: value'. Repeatable. Only with --http.",
        AllowMultipleArgumentsPerToken = false,
    };

    private readonly Option<string[]> _env = new("--env")
    {
        Description = "Environment variable for the stdio server as NAME=value. Repeatable. Only with --stdio.",
        AllowMultipleArgumentsPerToken = false,
    };

    private readonly Option<string> _cwd = new("--cwd")
    {
        Description = "Working directory for the stdio server.",
    };

    private readonly Option<int> _timeout = new("--timeout")
    {
        Description = "Seconds to wait for the server to start and list its tools.",
        DefaultValueFactory = _ => 60,
    };

    private readonly Option<string>? _file;

    public ServerOptions(bool allowFile = false)
    {
        if (allowFile)
        {
            _file = new Option<string>("--from-file")
            {
                Description = "Read the current tool surface from a snapshot file instead of a server.",
            };
        }
    }

    public void AddTo(Command command)
    {
        command.Options.Add(_stdio);
        command.Options.Add(_http);
        command.Options.Add(_transport);
        command.Options.Add(_headers);
        command.Options.Add(_env);
        command.Options.Add(_cwd);
        command.Options.Add(_timeout);
        if (_file is not null)
        {
            command.Options.Add(_file);
        }

        command.Validators.Add(result =>
        {
            var sources = new[] { result.GetResult(_stdio), result.GetResult(_http), _file is null ? null : result.GetResult(_file) }
                .Count(r => r is not null);
            if (sources != 1)
            {
                result.AddError(_file is null
                    ? "Pass exactly one of --stdio <command> or --http <url>."
                    : "Pass exactly one of --stdio <command>, --http <url> or --from-file <snapshot>.");
            }

            if (result.GetValue(_http) is { } url && ParseHttpUrl(url) is null)
            {
                result.AddError("--http must be an absolute http:// or https:// URL.");
            }

            if (result.GetResult(_transport) is { Implicit: false } transportResult)
            {
                if (result.GetResult(_http) is null)
                {
                    result.AddError("--transport can only be used with --http.");
                }

                var token = transportResult.Tokens.Count == 1 ? transportResult.Tokens[0].Value : null;
                if (ParseTransportMode(token) is null)
                {
                    result.AddError("--transport must be auto, streamable-http, or sse.");
                }
            }
        });
    }

    public async Task<ToolSurface> CaptureAsync(ParseResult parse, TextWriter stderr, CancellationToken cancellationToken)
    {
        if (_file is not null && parse.GetValue(_file) is { } file)
        {
            var path = Path.GetFullPath(file);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Snapshot file not found: {path}", path);
            }

            return ToolSurface.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
        }

        var timeoutSeconds = parse.GetValue(_timeout);
        if (timeoutSeconds <= 0)
        {
            throw new ArgumentException("--timeout must be a positive number of seconds.");
        }

        var target = new ServerTarget(
            parse.GetValue(_stdio),
            parse.GetValue(_http) is { } url ? ParseHttpUrl(url) : null,
            ParsePairs(parse.GetValue(_headers), ':', "--header", "Name: value"),
            ParsePairs(parse.GetValue(_env), '=', "--env", "NAME=value").ToDictionary(p => p.Key, p => (string?)p.Value, StringComparer.Ordinal),
            parse.GetValue(_cwd),
            ParseTransportMode(parse.GetValue(_transport)) ?? HttpTransportMode.AutoDetect,
            TimeSpan.FromSeconds(timeoutSeconds));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var serverStderr = new StderrBuffer();
        var challenge = new ChallengeCapture();
        try
        {
            var client = await ServerConnector.ConnectAsync(target, serverStderr, challenge, timeout.Token).ConfigureAwait(false);
            await using (client.ConfigureAwait(false))
            {
                return await ToolSurface.CaptureAsync(client, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException && serverStderr.ProcessStarted)
            {
                // A server that exits right away often fails the first write with "Broken pipe" before
                // its stderr lines have been delivered, so give them a moment to arrive.
                await serverStderr.WaitForLinesAsync(CancellationToken.None).ConfigureAwait(false);
            }

            if (serverStderr.ToString() is { Length: > 0 } lines)
            {
                await stderr.WriteLineAsync($"Server stderr (last lines):\n{CliApp.PrintableLines(lines.TrimEnd())}").ConfigureAwait(false);
            }

            if (ex is OperationCanceledException && timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // No inner exception: its message is the runtime's localized "A task was canceled.".
                throw new TimeoutException($"The server didn't respond within {timeoutSeconds} seconds.");
            }

            if (ex is HttpRequestException { StatusCode: not null } http && target.HttpEndpoint is { } endpoint)
            {
                // The SDK's message carries the response body. Say what to try instead, with no inner exception to bring the body back.
                var sentAuthorization = target.Headers.Keys.Any(k => string.Equals(k, "Authorization", StringComparison.OrdinalIgnoreCase));
                throw new HttpRequestException(ConnectError.Describe(http, endpoint, target.TransportMode, sentAuthorization, challenge.Challenge), inner: null, http.StatusCode);
            }

            throw;
        }
    }

    private static Uri? ParseHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? uri : null;

    private static HttpTransportMode? ParseTransportMode(string? value) => value?.ToLowerInvariant() switch
    {
        "auto" => HttpTransportMode.AutoDetect,
        "streamable-http" => HttpTransportMode.StreamableHttp,
        "sse" => HttpTransportMode.Sse,
        _ => null,
    };

    private static Dictionary<string, string> ParsePairs(string[]? values, char separator, string option, string format)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var value in values ?? [])
        {
            var index = value.IndexOf(separator, StringComparison.Ordinal);
            if (index <= 0)
            {
                throw new ArgumentException($"Invalid {option} '{value}'. Use {format}.");
            }

            result[value[..index].Trim()] = value[(index + 1)..].Trim();
        }

        return result;
    }
}
