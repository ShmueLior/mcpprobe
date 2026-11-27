using System.Text.Json.Nodes;
using McpProbe.Internal;

namespace McpProbe.Snapshots;

/// <summary>
/// Compares two tool surfaces and classifies every change as breaking or non-breaking.
/// </summary>
/// <remarks>
/// <para>Input schemas (what clients send) may widen but not narrow. Output schemas (what clients read) may narrow but not widen.</para>
/// <para>
/// Breaking: a removed tool, a removed parameter, a new required parameter, a parameter that became required,
/// a narrowed input type or a removed enum value. For output schemas: a removed output schema or property, a property that
/// became optional, a widened type or a new enum value.
/// </para>
/// <para>
/// Non-breaking: added tools, optional parameters and output properties, description, title and annotation changes,
/// and widened input types. Other JSON Schema keywords (<c>minimum</c>, <c>pattern</c>, <c>$ref</c> and so on) are
/// reported as <see cref="ToolSurfaceChangeKind.KeywordChanged"/> without being classified.
/// </para>
/// </remarks>
public static class ToolSurfaceComparer
{
    private static readonly HashSet<string> s_handledKeywords = new(StringComparer.Ordinal)
    {
        "type", "properties", "required", "enum", "items", "description",
    };

    /// <summary>Compares a baseline surface with the current one.</summary>
    /// <param name="baseline">The committed (expected) surface.</param>
    /// <param name="current">The surface the server exposes now.</param>
    /// <returns>The classified changes.</returns>
    public static ToolSurfaceDiff Compare(ToolSurface baseline, ToolSurface current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        var changes = new List<ToolSurfaceChange>();
        var oldTools = baseline.Tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var newTools = current.Tools.ToDictionary(t => t.Name, StringComparer.Ordinal);

        foreach (var old in baseline.Tools)
        {
            if (!newTools.TryGetValue(old.Name, out var cur))
            {
                changes.Add(new(old.Name, null, ToolSurfaceChangeKind.ToolRemoved, ChangeSeverity.Breaking, "Tool was removed."));
                continue;
            }

            CompareTool(old, cur, changes);
        }

        foreach (var cur in current.Tools)
        {
            if (!oldTools.ContainsKey(cur.Name))
            {
                changes.Add(new(cur.Name, null, ToolSurfaceChangeKind.ToolAdded, ChangeSeverity.NonBreaking, "Tool was added."));
            }
        }

        var ordered = changes
            .OrderByDescending(c => c.Severity)
            .ThenBy(c => c.ToolName, StringComparer.Ordinal)
            .ThenBy(c => c.Path ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(c => c.Kind)
            .ToList();
        return new ToolSurfaceDiff(ordered);
    }

    private static void CompareTool(ToolContract old, ToolContract cur, List<ToolSurfaceChange> changes)
    {
        var tool = old.Name;
        if (!string.Equals(old.Title, cur.Title, StringComparison.Ordinal))
        {
            changes.Add(new(tool, null, ToolSurfaceChangeKind.TitleChanged, ChangeSeverity.NonBreaking, "Title changed."));
        }

        if (!string.Equals(old.Description, cur.Description, StringComparison.Ordinal))
        {
            changes.Add(new(tool, null, ToolSurfaceChangeKind.DescriptionChanged, ChangeSeverity.NonBreaking, "Description changed."));
        }

        CompareAnnotations(tool, old.Annotations, cur.Annotations, changes);

        var context = new Context(tool, changes);
        CompareSchema(context, Side.Input, string.Empty, JsonSchemaReader.FromElement(old.InputSchema), JsonSchemaReader.FromElement(cur.InputSchema));

        switch (old.OutputSchema, cur.OutputSchema)
        {
            case (null, null):
                break;
            case (null, not null):
                changes.Add(new(tool, "output", ToolSurfaceChangeKind.OutputSchemaAdded, ChangeSeverity.NonBreaking, "Output schema was added."));
                break;
            case (not null, null):
                changes.Add(new(tool, "output", ToolSurfaceChangeKind.OutputSchemaRemoved, ChangeSeverity.Breaking, "Output schema was removed."));
                break;
            case ({ } o, { } c):
                CompareSchema(context, Side.Output, string.Empty, JsonSchemaReader.FromElement(o), JsonSchemaReader.FromElement(c));
                break;
        }
    }

    private static void CompareAnnotations(string tool, ToolContractAnnotations? old, ToolContractAnnotations? cur, List<ToolSurfaceChange> changes)
    {
        Check("readOnlyHint", old?.ReadOnlyHint, cur?.ReadOnlyHint);
        Check("destructiveHint", old?.DestructiveHint, cur?.DestructiveHint);
        Check("idempotentHint", old?.IdempotentHint, cur?.IdempotentHint);
        Check("openWorldHint", old?.OpenWorldHint, cur?.OpenWorldHint);

        void Check(string name, bool? before, bool? after)
        {
            if (before != after)
            {
                changes.Add(new(tool, null, ToolSurfaceChangeKind.AnnotationsChanged, ChangeSeverity.NonBreaking,
                    $"Annotation '{name}' changed from {Format(before)} to {Format(after)}."));
            }
        }

        static string Format(bool? value) => value switch
        {
            true => "true",
            false => "false",
            null => "unset",
        };
    }

    private static void CompareSchema(Context context, Side side, string path, JsonNode? old, JsonNode? cur)
    {
        CompareType(context, side, path, old, cur);
        CompareEnum(context, side, path, old, cur);

        if (path.Length > 0 &&
            !string.Equals(JsonSchemaReader.GetString(old, "description"), JsonSchemaReader.GetString(cur, "description"), StringComparison.Ordinal))
        {
            context.Add(side, path, ToolSurfaceChangeKind.DescriptionChanged, ChangeSeverity.NonBreaking, $"Description of {Describe(side, path)} changed.");
        }

        CompareProperties(context, side, path, old, cur);

        var oldItems = JsonSchemaReader.Get(old, "items");
        var curItems = JsonSchemaReader.Get(cur, "items");
        if (oldItems is JsonObject || curItems is JsonObject)
        {
            CompareSchema(context, side, path + "[]", oldItems as JsonObject, curItems as JsonObject);
        }

        CompareOtherKeywords(context, side, path, old, cur);
    }

    private static void CompareType(Context context, Side side, string path, JsonNode? old, JsonNode? cur)
    {
        var oldTypes = JsonSchemaReader.GetTypes(old);
        var curTypes = JsonSchemaReader.GetTypes(cur);
        if (SameTypes(oldTypes, curTypes))
        {
            return;
        }

        // Inputs may accept more than before; outputs may promise less than before.
        var compatible = side == Side.Input ? Accepts(curTypes, oldTypes) : Accepts(oldTypes, curTypes);
        var severity = compatible ? ChangeSeverity.NonBreaking : ChangeSeverity.Breaking;
        var subject = path.Length == 0 ? (side == Side.Input ? "Input schema" : "Output schema") : Capitalize(Describe(side, path));
        context.Add(side, path, ToolSurfaceChangeKind.TypeChanged, severity,
            $"{subject} type changed from {JsonSchemaReader.FormatTypes(oldTypes)} to {JsonSchemaReader.FormatTypes(curTypes)}.");
    }

    private static void CompareEnum(Context context, Side side, string path, JsonNode? old, JsonNode? cur)
    {
        var oldEnum = ReadEnum(old);
        var curEnum = ReadEnum(cur);
        if (oldEnum is null && curEnum is null)
        {
            return;
        }

        var subject = path.Length == 0 ? (side == Side.Input ? "the input schema" : "the output schema") : Describe(side, path);
        if (oldEnum is null)
        {
            // A new restriction: inputs that were accepted may now be rejected.
            context.Add(side, path, ToolSurfaceChangeKind.EnumChanged, side == Side.Input ? ChangeSeverity.Breaking : ChangeSeverity.NonBreaking,
                $"Allowed values were added to {subject}: {string.Join(", ", curEnum!)}.");
            return;
        }

        if (curEnum is null)
        {
            context.Add(side, path, ToolSurfaceChangeKind.EnumChanged, side == Side.Input ? ChangeSeverity.NonBreaking : ChangeSeverity.Breaking,
                $"The allowed-values restriction was removed from {subject}.");
            return;
        }

        var removed = oldEnum.Except(curEnum, StringComparer.Ordinal).ToList();
        var added = curEnum.Except(oldEnum, StringComparer.Ordinal).ToList();
        if (removed.Count > 0)
        {
            context.Add(side, path, ToolSurfaceChangeKind.EnumChanged, side == Side.Input ? ChangeSeverity.Breaking : ChangeSeverity.NonBreaking,
                $"Allowed values removed from {subject}: {string.Join(", ", removed)}.");
        }

        if (added.Count > 0)
        {
            context.Add(side, path, ToolSurfaceChangeKind.EnumChanged, side == Side.Input ? ChangeSeverity.NonBreaking : ChangeSeverity.Breaking,
                $"Allowed values added to {subject}: {string.Join(", ", added)}.");
        }
    }

    private static void CompareProperties(Context context, Side side, string path, JsonNode? old, JsonNode? cur)
    {
        var oldProps = JsonSchemaReader.GetProperties(old);
        var curProps = JsonSchemaReader.GetProperties(cur);
        var oldRequired = JsonSchemaReader.GetRequired(old);
        var curRequired = JsonSchemaReader.GetRequired(cur);
        if (oldProps is null && curProps is null && oldRequired.Count == 0 && curRequired.Count == 0)
        {
            return;
        }

        var oldNames = oldProps?.Select(p => p.Key).ToList() ?? [];
        var curNames = curProps?.Select(p => p.Key).ToList() ?? [];
        var added = curNames.Except(oldNames, StringComparer.Ordinal).ToList();

        foreach (var name in oldNames)
        {
            var childPath = Join(path, name);
            var oldChild = oldProps![name];
            if (curProps is null || !curProps.TryGetPropertyValue(name, out var curChild))
            {
                var hint = RenameHint(oldChild, added, curProps);
                context.Add(side, childPath, ToolSurfaceChangeKind.PropertyRemoved, ChangeSeverity.Breaking,
                    $"{Capitalize(Describe(side, childPath))} was removed{hint}.");
                continue;
            }

            var wasRequired = oldRequired.Contains(name);
            var isRequired = curRequired.Contains(name);
            if (!wasRequired && isRequired)
            {
                context.Add(side, childPath, ToolSurfaceChangeKind.BecameRequired, side == Side.Input ? ChangeSeverity.Breaking : ChangeSeverity.NonBreaking,
                    $"{Capitalize(Describe(side, childPath))} became required.");
            }
            else if (wasRequired && !isRequired)
            {
                context.Add(side, childPath, ToolSurfaceChangeKind.BecameOptional, side == Side.Input ? ChangeSeverity.NonBreaking : ChangeSeverity.Breaking,
                    $"{Capitalize(Describe(side, childPath))} became optional.");
            }

            CompareSchema(context, side, childPath, oldChild, curChild);
        }

        foreach (var name in added)
        {
            var childPath = Join(path, name);
            var required = curRequired.Contains(name);
            var severity = side == Side.Input && required ? ChangeSeverity.Breaking : ChangeSeverity.NonBreaking;
            context.Add(side, childPath, ToolSurfaceChangeKind.PropertyAdded, severity,
                $"{Capitalize(Describe(side, childPath))} was added ({(required ? "required" : "optional")}).");
        }

        // "required" may name properties that "properties" doesn't declare; requiring one still breaks callers.
        foreach (var name in oldRequired.Union(curRequired).Order(StringComparer.Ordinal))
        {
            if ((oldProps?.ContainsKey(name) ?? false) || (curProps?.ContainsKey(name) ?? false))
            {
                continue;
            }

            var childPath = Join(path, name);
            if (!oldRequired.Contains(name))
            {
                context.Add(side, childPath, ToolSurfaceChangeKind.BecameRequired, side == Side.Input ? ChangeSeverity.Breaking : ChangeSeverity.NonBreaking,
                    $"{Capitalize(Describe(side, childPath))} became required (it isn't declared in 'properties').");
            }
            else if (!curRequired.Contains(name))
            {
                context.Add(side, childPath, ToolSurfaceChangeKind.BecameOptional, side == Side.Input ? ChangeSeverity.NonBreaking : ChangeSeverity.Breaking,
                    $"{Capitalize(Describe(side, childPath))} became optional (it isn't declared in 'properties').");
            }
        }
    }

    private static void CompareOtherKeywords(Context context, Side side, string path, JsonNode? old, JsonNode? cur)
    {
        var oldObj = old as JsonObject;
        var curObj = cur as JsonObject;
        var keys = (oldObj?.Select(p => p.Key) ?? [])
            .Union(curObj?.Select(p => p.Key) ?? [], StringComparer.Ordinal)
            .Where(k => !s_handledKeywords.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal);

        foreach (var key in keys)
        {
            JsonNode? before = null;
            JsonNode? after = null;
            var hadBefore = oldObj is not null && oldObj.TryGetPropertyValue(key, out before);
            var hasAfter = curObj is not null && curObj.TryGetPropertyValue(key, out after);
            if (hadBefore == hasAfter && JsonNode.DeepEquals(before, after))
            {
                continue;
            }

            var subject = path.Length == 0 ? (side == Side.Input ? "the input schema" : "the output schema") : Describe(side, path);
            context.Add(side, path, ToolSurfaceChangeKind.KeywordChanged, ChangeSeverity.NonBreaking,
                $"Keyword '{key}' on {subject} changed from {Preview(hadBefore, before)} to {Preview(hasAfter, after)} (not classified, review by hand).");
        }
    }

    private static string RenameHint(JsonNode? removed, List<string> added, JsonObject? curProps)
    {
        if (curProps is null)
        {
            return string.Empty;
        }

        var removedTypes = JsonSchemaReader.GetTypes(removed);
        var candidates = added.Where(a => SameTypes(removedTypes, JsonSchemaReader.GetTypes(curProps[a]))).ToList();
        return candidates.Count == 1 ? $" (possibly renamed to '{candidates[0]}')" : string.Empty;
    }

    private static List<string>? ReadEnum(JsonNode? schema) =>
        JsonSchemaReader.Get(schema, "enum") is JsonArray array
            ? array.Select(JsonSchemaReader.ToCompactJson).ToList()
            : null;

    private static bool SameTypes(SortedSet<string>? a, SortedSet<string>? b) =>
        (a is null && b is null) || (a is not null && b is not null && a.SetEquals(b));

    /// <summary>Whether a schema with <paramref name="wider"/> types accepts every value of <paramref name="narrower"/> types.</summary>
    private static bool Accepts(SortedSet<string>? wider, SortedSet<string>? narrower)
    {
        if (wider is null)
        {
            return true;
        }

        if (narrower is null)
        {
            return false;
        }

        return narrower.All(t => wider.Contains(t) || (t == "integer" && wider.Contains("number")));
    }

    private static string Join(string path, string name) => path.Length == 0 ? name : path + "." + name;

    private static string Describe(Side side, string path) => side switch
    {
        Side.Input when !path.Contains('.', StringComparison.Ordinal) && !path.Contains('[', StringComparison.Ordinal) => $"parameter '{path}'",
        Side.Input => $"property '{path}'",
        _ => $"output property '{path}'",
    };

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string Preview(bool present, JsonNode? node)
    {
        if (!present)
        {
            return "(unset)";
        }

        var json = JsonSchemaReader.ToCompactJson(node);
        return json.Length <= 60 ? json : json[..57] + "...";
    }

    private enum Side
    {
        Input,
        Output,
    }

    private sealed class Context(string tool, List<ToolSurfaceChange> changes)
    {
        public void Add(Side side, string path, ToolSurfaceChangeKind kind, ChangeSeverity severity, string message)
        {
            var prefix = side == Side.Input ? "input" : "output";
            var fullPath = path.Length == 0 ? prefix : path.StartsWith('[') ? prefix + path : prefix + "." + path;
            changes.Add(new ToolSurfaceChange(tool, fullPath, kind, severity, message));
        }
    }
}
