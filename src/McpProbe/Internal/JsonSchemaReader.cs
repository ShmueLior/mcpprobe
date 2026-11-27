using System.Globalization;
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

    /// <summary>
    /// Resolves a local <c>$ref</c> (a JSON pointer such as <c>#/$defs/address</c> or <c>#/properties/root</c>) against
    /// <paramref name="root"/> and returns the schema to compare in its place: the target, with the ref's sibling
    /// <c>description</c> and <c>title</c> on top, and other siblings only where the target doesn't set them.
    /// Remote refs, anchors, broken pointers and ref chains that loop aren't resolved.
    /// </summary>
    /// <param name="schema">The schema that may hold a <c>$ref</c>.</param>
    /// <param name="root">The root schema the pointer is relative to.</param>
    /// <param name="resolved">The schema to use instead of <paramref name="schema"/>.</param>
    /// <param name="reference">The ref as written, used to stop on recursive schemas.</param>
    public static bool TryResolveRef(JsonNode? schema, JsonNode? root, out JsonNode? resolved, out string? reference)
    {
        resolved = null;
        reference = GetString(schema, "$ref");
        if (reference is null || !TryFindTarget(reference, root, out var target))
        {
            reference = null;
            return false;
        }

        if (target is not JsonObject targetObj)
        {
            resolved = target?.DeepClone();
            return true;
        }

        var result = (JsonObject)targetObj.DeepClone();
        foreach (var (key, value) in (JsonObject)schema!)
        {
            if (key != "$ref" && (key is "description" or "title" || !result.ContainsKey(key)))
            {
                result[key] = value?.DeepClone();
            }
        }

        resolved = result;
        return true;
    }

    /// <summary>Whether every <c>$ref</c> in <paramref name="root"/> resolves with <see cref="TryResolveRef"/>.</summary>
    public static bool AllRefsResolve(JsonNode? root)
    {
        return Walk(root);

        bool Walk(JsonNode? node) => node switch
        {
            JsonObject obj => (GetString(obj, "$ref") is not { } reference || TryFindTarget(reference, root, out _)) && obj.All(p => Walk(p.Value)),
            JsonArray array => array.All(Walk),
            _ => true,
        };
    }

    /// <summary>Finds the schema a local <c>$ref</c> points to, following ref chains, without copying it.</summary>
    public static bool TryFindTarget(string reference, JsonNode? root, out JsonNode? target)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        target = null;
        for (var r = reference; r is not null; r = GetString(target, "$ref"))
        {
            if (!seen.Add(r) || !TryFollowPointer(root, r, out target))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryFollowPointer(JsonNode? root, string reference, out JsonNode? target)
    {
        target = root;
        if (reference == "#")
        {
            return true;
        }

        if (!reference.StartsWith("#/", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var raw in reference[2..].Split('/'))
        {
            var token = Uri.UnescapeDataString(raw).Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            switch (target)
            {
                case JsonObject obj when obj.TryGetPropertyValue(token, out var child):
                    target = child;
                    break;
                case JsonArray array when int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < array.Count:
                    target = array[index];
                    break;
                default:
                    target = null;
                    return false;
            }
        }

        return true;
    }

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
