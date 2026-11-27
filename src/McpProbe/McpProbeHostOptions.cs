using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;

namespace McpProbe;

/// <summary>
/// Options for <see cref="McpProbeHost"/>.
/// </summary>
public sealed class McpProbeHostOptions
{
    /// <summary>
    /// Registers extra services the server's tools, prompts or resources depend on.
    /// Runs before the server builder callback.
    /// </summary>
    public Action<IServiceCollection>? ConfigureServices { get; set; }

    /// <summary>
    /// Configures the server options (server info, instructions, capabilities).
    /// </summary>
    public Action<McpServerOptions>? ConfigureServer { get; set; }

    /// <summary>
    /// Options for the connected client. When <see langword="null"/>, SDK defaults are used.
    /// </summary>
    public McpClientOptions? ClientOptions { get; set; }

    /// <summary>
    /// Logger factory used by both the server and the client. Defaults to a no-op logger factory.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; set; }
}
