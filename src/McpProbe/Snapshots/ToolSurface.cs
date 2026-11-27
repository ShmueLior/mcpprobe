using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using McpProbe.Internal;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpProbe.Snapshots;

/// <summary>
/// A server's tool surface: every tool's name, description, input schema, output schema and annotations.
/// This is what gets written to a snapshot file and compared between versions.
/// </summary>
public sealed class ToolSurface
{
    /// <summary>The snapshot file format version this library writes.</summary>
    public const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions s_writeOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private ToolSurface(IReadOnlyList<ToolContract> tools) => Tools = tools;

    /// <summary>Gets the tools, sorted by name (ordinal).</summary>
    public IReadOnlyList<ToolContract> Tools { get; }

    /// <summary>Creates a surface from protocol tools (for example the result of <c>tools/list</c>).</summary>
    /// <param name="tools">The tools.</param>
    /// <returns>The surface.</returns>
    /// <exception cref="ArgumentException">Two tools have the same name.</exception>
    public static ToolSurface FromTools(IEnumerable<Tool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var contracts = new List<ToolContract>();
        foreach (var tool in tools)
        {
            var annotations = tool.Annotations is { } a &&
                (a.ReadOnlyHint is not null || a.DestructiveHint is not null || a.IdempotentHint is not null || a.OpenWorldHint is not null)
                ? new ToolContractAnnotations(a.ReadOnlyHint, a.DestructiveHint, a.IdempotentHint, a.OpenWorldHint)
                : null;

            contracts.Add(new ToolContract(
                tool.Name,
                tool.Title ?? tool.Annotations?.Title,
                tool.Description,
                CanonicalElement(tool.InputSchema) ?? EmptyObjectSchema(),
                tool.OutputSchema is { } output ? CanonicalElement(output) : null,
                annotations));
        }

        return Create(contracts);
    }

    /// <summary>Creates a surface from client tools (the result of <c>McpClient.ListToolsAsync</c>).</summary>
    /// <param name="tools">The tools.</param>
    /// <returns>The surface.</returns>
    public static ToolSurface FromClientTools(IEnumerable<McpClientTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        return FromTools(tools.Select(t => t.ProtocolTool));
    }

    /// <summary>Lists all tools from a connected client and captures their surface.</summary>
    /// <param name="client">The client.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The surface.</returns>
    public static async Task<ToolSurface> CaptureAsync(McpClient client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return FromClientTools(tools);
    }

    /// <summary>Parses a snapshot file's content.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The surface.</returns>
    /// <exception cref="FormatException">The text is not a valid McpProbe snapshot.</exception>
    public static ToolSurface Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"The snapshot is not valid JSON: {ex.Message}", ex);
        }

        if (root is not JsonObject obj)
        {
            throw new FormatException("The snapshot must be a JSON object.");
        }

        if (obj["formatVersion"] is not JsonValue versionNode || !versionNode.TryGetValue(out int version))
        {
            throw new FormatException("The snapshot has no 'formatVersion'.");
        }

        if (version != CurrentFormatVersion)
        {
            throw new FormatException($"Snapshot format version {version} is not supported. This version of McpProbe reads version {CurrentFormatVersion}.");
        }

        if (obj["tools"] is not JsonArray toolsNode)
        {
            throw new FormatException("The snapshot has no 'tools' array.");
        }

        var contracts = new List<ToolContract>();
        foreach (var item in toolsNode)
        {
            if (item is not JsonObject tool || JsonSchemaReader.GetString(tool, "name") is not { Length: > 0 } name)
            {
                throw new FormatException("Every tool in the snapshot needs a non-empty 'name'.");
            }

            var input = tool["inputSchema"] is JsonObject inputNode ? ToElement(inputNode) : EmptyObjectSchema();
            JsonElement? output = tool["outputSchema"] is JsonObject outputNode ? ToElement(outputNode) : null;
            ToolContractAnnotations? annotations = null;
            if (tool["annotations"] is JsonObject a)
            {
                annotations = new ToolContractAnnotations(ReadBool(a, "readOnlyHint"), ReadBool(a, "destructiveHint"), ReadBool(a, "idempotentHint"), ReadBool(a, "openWorldHint"));
            }

            contracts.Add(new ToolContract(name, JsonSchemaReader.GetString(tool, "title"), JsonSchemaReader.GetString(tool, "description"), input, output, annotations));
        }

        try
        {
            return Create(contracts);
        }
        catch (ArgumentException ex)
        {
            throw new FormatException(ex.Message, ex);
        }
    }

    /// <summary>Serializes the surface in the snapshot file format (indented, LF line endings, stable key order).</summary>
    /// <returns>The JSON text, ending with a newline.</returns>
    public string ToJson()
    {
        var tools = new JsonArray();
        foreach (var tool in Tools)
        {
            var node = new JsonObject { ["name"] = tool.Name };
            if (tool.Title is not null)
            {
                node["title"] = tool.Title;
            }

            if (tool.Description is not null)
            {
                node["description"] = tool.Description;
            }

            node["inputSchema"] = JsonSchemaReader.FromElement(tool.InputSchema);
            if (tool.OutputSchema is { } output)
            {
                node["outputSchema"] = JsonSchemaReader.FromElement(output);
            }

            if (tool.Annotations is { } a)
            {
                var annotations = new JsonObject();
                AddBool(annotations, "destructiveHint", a.DestructiveHint);
                AddBool(annotations, "idempotentHint", a.IdempotentHint);
                AddBool(annotations, "openWorldHint", a.OpenWorldHint);
                AddBool(annotations, "readOnlyHint", a.ReadOnlyHint);
                node["annotations"] = annotations;
            }

            tools.Add(node);
        }

        var root = new JsonObject
        {
            ["formatVersion"] = CurrentFormatVersion,
            ["tools"] = tools,
        };

        // Older runtimes use the platform newline when indenting; snapshots are always LF.
        return root.ToJsonString(s_writeOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static ToolSurface Create(List<ToolContract> contracts)
    {
        contracts.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
        for (var i = 1; i < contracts.Count; i++)
        {
            if (string.Equals(contracts[i - 1].Name, contracts[i].Name, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Duplicate tool name '{contracts[i].Name}'.", nameof(contracts));
            }
        }

        return new ToolSurface(contracts);
    }

    private static JsonElement? CanonicalElement(JsonElement element) =>
        JsonSchemaReader.Canonicalize(JsonSchemaReader.FromElement(element)) is { } node ? ToElement(node) : null;

    private static JsonElement ToElement(JsonNode node)
    {
        using var doc = JsonDocument.Parse(JsonSchemaReader.Canonicalize(node)!.ToJsonString());
        return doc.RootElement.Clone();
    }

    private static JsonElement EmptyObjectSchema()
    {
        using var doc = JsonDocument.Parse("""{"type":"object"}""");
        return doc.RootElement.Clone();
    }

    private static bool? ReadBool(JsonObject obj, string name) =>
        obj[name] is JsonValue v && v.TryGetValue(out bool b) ? b : null;

    private static void AddBool(JsonObject obj, string name, bool? value)
    {
        if (value is { } b)
        {
            obj[name] = b;
        }
    }
}
