using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using McpProbe.Linting;
using McpProbe.Snapshots;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpProbe;

/// <summary>
/// Runs an MCP server built with the official SDK in memory and connects a client to it.
/// </summary>
/// <remarks>
/// The server and client talk over in-process pipes, using the same JSON-RPC code paths as a real
/// stdio connection. Dispose the host to stop the server.
/// </remarks>
/// <example>
/// <code>
/// await using var probe = await McpProbeHost.StartAsync(mcp => mcp.WithTools&lt;MyTools&gt;());
/// var tools = await probe.Client.ListToolsAsync();
/// </code>
/// </example>
public sealed class McpProbeHost : IAsyncDisposable
{
    private readonly Pipe _clientToServer;
    private readonly Pipe _serverToClient;
    private readonly ServiceProvider _services;
    private readonly CancellationTokenSource _serverCts;
    private readonly Task _serverTask;
    private McpClient? _client;
    private int _disposed;

    private McpProbeHost(Pipe clientToServer, Pipe serverToClient, ServiceProvider services, McpServer server, CancellationTokenSource serverCts, Task serverTask)
    {
        _clientToServer = clientToServer;
        _serverToClient = serverToClient;
        _services = services;
        Server = server;
        _serverCts = serverCts;
        _serverTask = serverTask;
    }

    /// <summary>
    /// Gets the client connected to the in-memory server.
    /// </summary>
    public McpClient Client => _client ?? throw new InvalidOperationException("The client is not connected yet.");

    /// <summary>
    /// Gets the in-memory server instance.
    /// </summary>
    public McpServer Server { get; }

    /// <summary>
    /// Gets the server's service provider.
    /// </summary>
    public IServiceProvider Services => _services;

    /// <summary>
    /// Builds and starts an in-memory MCP server and returns a host with a connected client.
    /// </summary>
    /// <param name="configureServer">Registers tools, prompts, resources and handlers, for example <c>mcp =&gt; mcp.WithTools&lt;MyTools&gt;()</c>.</param>
    /// <param name="cancellationToken">Cancels the connection handshake.</param>
    /// <returns>A started host. Dispose it to stop the server.</returns>
    public static Task<McpProbeHost> StartAsync(Action<IMcpServerBuilder> configureServer, CancellationToken cancellationToken = default) =>
        StartAsync(configureServer, options: null, cancellationToken);

    /// <summary>
    /// Builds and starts an in-memory MCP server and returns a host with a connected client.
    /// </summary>
    /// <param name="configureServer">Registers tools, prompts, resources and handlers, for example <c>mcp =&gt; mcp.WithTools&lt;MyTools&gt;()</c>.</param>
    /// <param name="options">Extra services, server options, client options and logging.</param>
    /// <param name="cancellationToken">Cancels the connection handshake.</param>
    /// <returns>A started host. Dispose it to stop the server.</returns>
    public static async Task<McpProbeHost> StartAsync(Action<IMcpServerBuilder> configureServer, McpProbeHostOptions? options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configureServer);

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var loggerFactory = options?.LoggerFactory;

        var services = new ServiceCollection();
        options?.ConfigureServices?.Invoke(services);

        var builder = services
            .AddMcpServer(serverOptions => options?.ConfigureServer?.Invoke(serverOptions))
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());
        configureServer(builder);

        // Tools may take ILogger<T>; make that resolvable even when the caller registered no logging.
        if (loggerFactory is not null)
        {
            services.Replace(ServiceDescriptor.Singleton(loggerFactory));
        }

        services.TryAddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.TryAdd(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(NullLogger<>)));

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var serverCts = new CancellationTokenSource();
        McpProbeHost? host = null;
        try
        {
            var server = provider.GetRequiredService<McpServer>();
            var serverTask = server.RunAsync(serverCts.Token);
            host = new McpProbeHost(clientToServer, serverToClient, provider, server, serverCts, serverTask);

            var transport = new StreamClientTransport(
                serverInput: clientToServer.Writer.AsStream(),
                serverOutput: serverToClient.Reader.AsStream(),
                loggerFactory);

            host._client = await McpClient.CreateAsync(transport, options?.ClientOptions, loggerFactory, cancellationToken).ConfigureAwait(false);
            return host;
        }
        catch
        {
            // Clean up, but keep the original exception: it says why the server didn't start.
            try
            {
                if (host is not null)
                {
                    await host.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    serverCts.Dispose();
                    await provider.DisposeAsync().ConfigureAwait(false);
                }
            }
#pragma warning disable CA1031 // A cleanup failure must not hide the startup failure.
            catch (Exception)
#pragma warning restore CA1031
            {
            }

            throw;
        }
    }

    /// <summary>
    /// Checks the server's tool surface against a snapshot file. See <see cref="ToolSnapshot"/> for the workflow.
    /// </summary>
    /// <param name="snapshotPath">The snapshot path. Relative paths resolve against <see cref="SnapshotOptions.Directory"/> or the calling source file's directory.</param>
    /// <param name="options">Snapshot options.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <param name="callerFilePath">Filled in by the compiler.</param>
    /// <returns>The diff between the snapshot and the current surface.</returns>
    /// <exception cref="McpProbeSnapshotException">The check failed.</exception>
    public Task<ToolSurfaceDiff> VerifyToolSnapshotAsync(
        string snapshotPath = ToolSnapshot.DefaultFileName,
        SnapshotOptions? options = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string callerFilePath = "") =>
        ToolSnapshot.VerifyAsync(Client, snapshotPath, options, cancellationToken, callerFilePath);

    /// <summary>
    /// Checks the server's tool surface against a snapshot file, using the default file name.
    /// See <see cref="ToolSnapshot"/> for the workflow.
    /// </summary>
    /// <param name="snapshotOptions">Snapshot options.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <param name="callerFilePath">Filled in by the compiler.</param>
    /// <returns>The diff between the snapshot and the current surface.</returns>
    /// <exception cref="McpProbeSnapshotException">The check failed.</exception>
    public Task<ToolSurfaceDiff> VerifyToolSnapshotAsync(
        SnapshotOptions snapshotOptions,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string callerFilePath = "")
    {
        ArgumentNullException.ThrowIfNull(snapshotOptions);
        return VerifyToolSnapshotAsync(ToolSnapshot.DefaultFileName, snapshotOptions, cancellationToken, callerFilePath);
    }

    /// <summary>
    /// Lists the server's tools and lints them.
    /// </summary>
    /// <param name="options">Lint options.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The lint report. Call <see cref="LintReport.ThrowIfFailed"/> to fail a test on findings.</returns>
    public async Task<LintReport> LintToolsAsync(LintOptions? options = null, CancellationToken cancellationToken = default)
    {
        var surface = await ToolSurface.CaptureAsync(Client, cancellationToken).ConfigureAwait(false);
        return ToolLinter.Lint(surface, options);
    }

    /// <summary>
    /// Stops the server and disposes the client and the server's services.
    /// </summary>
    /// <returns>A task that completes when the server has stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }

        await _serverCts.CancelAsync().ConfigureAwait(false);
        await _clientToServer.Writer.CompleteAsync().ConfigureAwait(false);
        await _serverToClient.Writer.CompleteAsync().ConfigureAwait(false);

        try
        {
            await _serverTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected: the server stops when its token is canceled.
        }

        await _services.DisposeAsync().ConfigureAwait(false);
        _serverCts.Dispose();
    }
}
