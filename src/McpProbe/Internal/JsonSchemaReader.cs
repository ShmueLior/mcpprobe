using System.Text.Json;
using System.Text.Json.Nodes;

namespace McpProbe.Internal;

/// <summary>
/// Small, allocation-light readers for the JSON Schema shapes MCP tools use.
/// </summary>
internal static class JsonSchemaReader
{
    /// <summary>Returns the declared types, or <see langword="null"/> when the schema doesn't restrict the type.</summary>
    public static SortedSet<string>? GetTypes(JsonNode? schema)
    {
        if (schema is not JsonObject obj || !obj.TryGetPropertyValue("type", out var type) || type is null)
        {
            return null;
        }

        var result = new SortedSet<string>(StringComparer.Ordinal);
        switch (type)
        {
            case JsonValue value when value.TryGetValue(out string? single):
                result.Add(single);
                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is JsonValue v && v.TryGetValue(out string? s))
                    {
                        result.Add(s);
                    }
                }

                break;
        }

        return result.Count == 0 ? null : result;
    }

    public static JsonObject? GetProperties(JsonNode? schema) =>
        schema is JsonObject obj && obj.TryGetPropertyValue("properties", out var props) ? props as JsonObject : null;

    public static HashSet<string> GetRequired(JsonNode? schema)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (schema is JsonObject obj && obj.TryGetPropertyValue("required", out var required) && required is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is JsonValue v && v.TryGetValue(out string? s))
                {
                    result.Add(s);
                }
            }
        }

        return result;
    }

    public static string? GetString(JsonNode? schema, string keyword) =>
        schema is JsonObject obj && obj.TryGetPropertyValue(keyword, out var node) && node is JsonValue value && value.TryGetValue(out string? s)
            ? s
            : null;

    public static JsonNode? Get(JsonNode? schema, string keyword) =>
        schema is JsonObject obj && obj.TryGetPropertyValue(keyword, out var node) ? node : null;

    public static string FormatTypes(SortedSet<string>? types) =>
        types is null ? "any" : string.Join(" | ", types);

    /// <summary>
    /// Returns a deep copy with object keys sorted (ordinal), so snapshots are stable regardless of emit order.
    /// Array order is kept.
    /// </summary>
    public static JsonNode? Canonicalize(JsonNode? node) => node switch
    {
        null => null,
        JsonObject obj => CanonicalizeObject(obj),
        JsonArray array => new JsonArray(array.Select(Canonicalize).ToArray()),
        _ => node.DeepClone(),
    };

    public static JsonNode? FromElement(JsonElement element) =>
        element.ValueKind is JsonValueKind.Undefined ? null : JsonNode.Parse(element.GetRawText());

    public static string ToCompactJson(JsonNode? node) => node?.ToJsonString() ?? "null";

    private static JsonObject CanonicalizeObject(JsonObject obj)
    {
        var result = new JsonObject();
        foreach (var (key, value) in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            result[key] = Canonicalize(value);
        }

        return result;
    }
}
