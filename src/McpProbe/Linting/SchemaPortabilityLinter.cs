using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using McpProbe.Internal;
using McpProbe.Snapshots;

namespace McpProbe.Linting;

/// <summary>
/// Ports MCP Inspector's schema portability walk (<c>core/json/schemaLint.ts</c>): boolean-schema,
/// type-union, untyped-schema and remote-ref. Used by MCPP009-MCPP012 when
/// <see cref="LintOptions.Strict"/> is on, or when a caller overrides those rules in
/// <see cref="LintOptions.Severities"/>.
/// </summary>
internal static class SchemaPortabilityLinter
{
    // Keyword lists and predicates follow Inspector schemaLint.ts, so a schema that is
    // portable there is portable here too.
    private const int MaxDepth = 64;

    private static readonly HashSet<string> BooleanValuedKeywords = new(StringComparer.Ordinal)
    {
        "additionalProperties",
        "unevaluatedProperties",
        "additionalItems",
        "unevaluatedItems",
    };

    private static readonly string[] SubschemaKeywords =
    [
        "items",
        "contains",
        "not",
        "propertyNames",
        "if",
        "then",
        "else",
        "additionalProperties",
        "unevaluatedProperties",
        "additionalItems",
        "unevaluatedItems",
        "contentSchema",
    ];

    private static readonly string[] SubschemaArrayKeywords = ["allOf", "anyOf", "oneOf", "prefixItems"];

    private static readonly string[] SubschemaMapKeywords =
    [
        "properties",
        "patternProperties",
        "dependentSchemas",
        "dependencies",
        "$defs",
        "definitions",
    ];

    private static readonly HashSet<string> ConstrainingKeywords = new(StringComparer.Ordinal)
    {
        "type", "enum", "const",
        "multipleOf", "maximum", "exclusiveMaximum", "minimum", "exclusiveMinimum",
        "maxLength", "minLength", "pattern",
        "format", "contentMediaType", "contentEncoding", "contentSchema",
        "maxItems", "minItems", "uniqueItems", "maxContains", "minContains",
        "maxProperties", "minProperties", "required", "dependentRequired",
        "allOf", "anyOf", "oneOf", "not",
        "items", "prefixItems", "contains", "additionalItems", "unevaluatedItems",
        "properties", "patternProperties", "additionalProperties", "unevaluatedProperties",
        "propertyNames", "dependentSchemas", "dependencies",
        "$ref", "$dynamicRef", "$recursiveRef",
    };

    private static readonly HashSet<string> JsonSchemaTypes = new(StringComparer.Ordinal)
    {
        "null", "boolean", "object", "array", "number", "string", "integer",
    };

    public static void Lint(ToolContract tool, LintOptions options, List<LintFinding> findings)
    {
        if (!AnyEnabled(options))
        {
            return;
        }

        WalkSchema(JsonSchemaReader.FromElement(tool.InputSchema), "inputSchema", tool.Name, options, findings);
        if (tool.OutputSchema is { } output)
        {
            WalkSchema(JsonSchemaReader.FromElement(output), "outputSchema", tool.Name, options, findings);
        }
    }

    private static bool AnyEnabled(LintOptions options) =>
        options.IsPortabilityRuleEnabled(LintRules.BooleanSchema) ||
        options.IsPortabilityRuleEnabled(LintRules.TypeUnion) ||
        options.IsPortabilityRuleEnabled(LintRules.UntypedSchema) ||
        options.IsPortabilityRuleEnabled(LintRules.RemoteRef);

    private static void WalkSchema(JsonNode? schema, string kind, string tool, LintOptions options, List<LintFinding> findings)
    {
        var ctx = new WalkContext(kind, tool, options, findings, DeclaresAnyId(schema, 0));
        Walk(schema, path: "", keyword: null, depth: 0, ctx);
    }

    private static void Walk(JsonNode? node, string path, string? keyword, int depth, WalkContext ctx)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        if (TryGetBoolean(node, out var boolean))
        {
            if (keyword is not null && BooleanValuedKeywords.Contains(keyword))
            {
                return;
            }

            if (ctx.Options.IsPortabilityRuleEnabled(LintRules.BooleanSchema))
            {
                var suggestion = boolean
                    ? TrueSuggestion(keyword)
                    : "Use the object form of the always-false schema, {\"not\": {}}. Deleting the entry is a different contract: under the default additionalProperties the property would then be allowed with any value, not forbidden.";
                Add(ctx, LintRules.BooleanSchema, path,
                    $"Bare '{(boolean ? "true" : "false")}' used where a schema object is expected. {suggestion}");
            }

            return;
        }

        if (node is not JsonObject obj)
        {
            return;
        }

        LintNode(obj, path, keyword, ctx);
        ForEachSubschema(obj, path, (child, childPath, childKeyword) => Walk(child, childPath, childKeyword, depth + 1, ctx));
    }

    private static void LintNode(JsonObject node, string path, string? keyword, WalkContext ctx)
    {
        if (TryGetPortableTypeUnion(node, out var types) && ctx.Options.IsPortabilityRuleEnabled(LintRules.TypeUnion))
        {
            var rendered = string.Join(",", types.Select(t => "{\"type\":\"" + t + "\"}"));
            var caveat = types.Contains("null", StringComparer.Ordinal)
                ? " OpenAPI 3.0 writes nullability as nullable: true, which is not JSON Schema."
                : "";
            Add(ctx, LintRules.TypeUnion, path,
                $"`type` is an array ({FormatTypeArray(types)}). This is legal JSON Schema but less portable: a client that expects a single type may reject the tool. Split it into anyOf of single types: {{\"anyOf\":[{rendered}]}}. Optional is a different contract: absent is not null.{caveat}");
        }

        var reference = JsonSchemaReader.GetString(node, "$ref");
        if (reference is { Length: > 0 } &&
            !reference.StartsWith('#') &&
            !ctx.HasEmbeddedIds &&
            ctx.Options.IsPortabilityRuleEnabled(LintRules.RemoteRef))
        {
            Add(ctx, LintRules.RemoteRef, path,
                $"`$ref` points outside this document ('{reference}'). Clients do not fetch remote schemas, so the constraint is dropped or the tool is rejected. Inline the referenced schema, or move it into $defs and reference it as #/$defs/<name>.");
        }

        if (!ConstrainsInstance(node) &&
            keyword != "not" &&
            ctx.Options.IsPortabilityRuleEnabled(LintRules.UntypedSchema))
        {
            Add(ctx, LintRules.UntypedSchema, path,
                "Schema carries no validation keyword at all, so it accepts any value (the object form of a bare true). Declare what the value actually is, for example {\"type\": \"object\", \"additionalProperties\": true} for a free-form object. That is a deliberate change of contract, which is the point: as written it constrains nothing.");
        }
    }

    private static string TrueSuggestion(string? keyword) =>
        keyword == "not"
            ? "Under `not`, use {}, the object form of the always-true schema. Declaring a concrete type here would instead let every value of another type through."
            : "Declare what the value actually is, for example {\"type\": \"object\", \"additionalProperties\": true} for a free-form object. That is a deliberate change of contract, not an equivalent rewrite: true accepts any JSON value at all.";

    private static bool ConstrainsInstance(JsonObject node)
    {
        foreach (var (key, _) in node)
        {
            if (ConstrainingKeywords.Contains(key))
            {
                return true;
            }
        }

        return node.ContainsKey("if") && (node.ContainsKey("then") || node.ContainsKey("else"));
    }

    private static bool TryGetPortableTypeUnion(JsonObject node, out List<string> types)
    {
        types = [];
        if (!node.TryGetPropertyValue("type", out var type) || type is not JsonArray array || array.Count == 0)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in array)
        {
            if (item is not JsonValue value ||
                !value.TryGetValue(out string? name) ||
                !JsonSchemaTypes.Contains(name) ||
                !seen.Add(name))
            {
                types = [];
                return false;
            }

            types.Add(name);
        }

        return true;
    }

    private static string FormatTypeArray(List<string> types) =>
        "[" + string.Join(",", types.Select(t => "\"" + t + "\"")) + "]";

    private static bool DeclaresAnyId(JsonNode? node, int depth)
    {
        if (depth > MaxDepth || node is not JsonObject obj)
        {
            return false;
        }

        if (JsonSchemaReader.GetString(obj, "$id") is { Length: > 0 })
        {
            return true;
        }

        var found = false;
        ForEachSubschema(obj, "", (child, _, _) =>
        {
            if (!found)
            {
                found = DeclaresAnyId(child, depth + 1);
            }
        });
        return found;
    }

    private static void ForEachSubschema(JsonObject node, string path, Action<JsonNode?, string, string> visit)
    {
        foreach (var key in SubschemaMapKeywords)
        {
            if (JsonSchemaReader.Get(node, key) is not JsonObject map)
            {
                continue;
            }

            foreach (var (name, sub) in map)
            {
                visit(sub, ChildPath(ChildPath(path, key), name), key);
            }
        }

        foreach (var key in SubschemaArrayKeywords)
        {
            if (JsonSchemaReader.Get(node, key) is not JsonArray array)
            {
                continue;
            }

            for (var i = 0; i < array.Count; i++)
            {
                visit(array[i], IndexPath(ChildPath(path, key), i), key);
            }
        }

        foreach (var key in SubschemaKeywords)
        {
            if (!node.ContainsKey(key))
            {
                continue;
            }

            var value = JsonSchemaReader.Get(node, key);
            if (key == "items" && value is JsonArray items)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    visit(items[i], IndexPath(ChildPath(path, key), i), key);
                }

                continue;
            }

            visit(value, ChildPath(path, key), key);
        }
    }

    private static void Add(WalkContext ctx, LintRule rule, string path, string message)
    {
        var severity = ctx.Options.SeverityOf(rule);
        if (severity != LintSeverity.None)
        {
            ctx.Findings.Add(new LintFinding(rule.Id, severity, ctx.Tool, DescribeSchemaPath(ctx.Kind, path), message));
        }
    }

    private static string DescribeSchemaPath(string kind, string path) =>
        path.Length == 0 ? kind : kind + "." + path;

    private static string ChildPath(string parent, string key)
    {
        if (IsBareIdentifier(key))
        {
            return parent.Length == 0 ? key : parent + "." + key;
        }

        return parent + "[" + JsonSerializer.Serialize(key) + "]";
    }

    private static string IndexPath(string parent, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{parent}[{index}]");

    private static bool IsBareIdentifier(string key)
    {
        if (key.Length == 0 || !IsIdentStart(key[0]))
        {
            return false;
        }

        for (var i = 1; i < key.Length; i++)
        {
            if (!IsIdentPart(key[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentStart(char c) =>
        c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_' or '$';

    private static bool IsIdentPart(char c) => IsIdentStart(c) || c is >= '0' and <= '9';

    private static bool TryGetBoolean(JsonNode? node, out bool value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue(out bool flag))
        {
            value = flag;
            return true;
        }

        value = default;
        return false;
    }

    private sealed class WalkContext(string kind, string tool, LintOptions options, List<LintFinding> findings, bool hasEmbeddedIds)
    {
        public string Kind { get; } = kind;

        public string Tool { get; } = tool;

        public LintOptions Options { get; } = options;

        public List<LintFinding> Findings { get; } = findings;

        public bool HasEmbeddedIds { get; } = hasEmbeddedIds;
    }
}
