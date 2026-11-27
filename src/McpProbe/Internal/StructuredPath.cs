using System.Globalization;
using System.Text.Json;

namespace McpProbe.Internal;

/// <summary>Navigates paths like <c>a.b[0].c</c> inside a JSON element.</summary>
internal static class StructuredPath
{
    public static JsonElement Navigate(JsonElement root, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var current = root;
        var walked = string.Empty;
        var i = 0;
        while (i < path.Length)
        {
            if (path[i] == '[')
            {
                var end = path.IndexOf(']', i);
                if (end < 0 || !int.TryParse(path.AsSpan(i + 1, end - i - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                {
                    throw new ArgumentException($"Invalid index in path '{path}' at position {i}.", nameof(path));
                }

                if (current.ValueKind != JsonValueKind.Array || index >= current.GetArrayLength())
                {
                    throw Fail.With($"Structured content has no element '{walked}[{index}]'. Value at '{Display(walked)}': {Fail.Truncate(current.GetRawText(), 200)}");
                }

                current = current[index];
                walked += $"[{index}]";
                i = end + 1;
                if (i < path.Length && path[i] == '.')
                {
                    i++;
                }

                continue;
            }

            var next = path.IndexOfAny(['.', '['], i);
            var name = next < 0 ? path[i..] : path[i..next];
            if (name.Length == 0)
            {
                throw new ArgumentException($"Empty segment in path '{path}' at position {i}.", nameof(path));
            }

            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out var child))
            {
                var available = current.ValueKind == JsonValueKind.Object
                    ? Fail.List(current.EnumerateObject().Select(p => p.Name))
                    : $"(not an object: {current.ValueKind})";
                throw Fail.With($"Structured content has no property '{(walked.Length == 0 ? name : walked + "." + name)}'. Properties at '{Display(walked)}': {available}.");
            }

            current = child;
            walked = walked.Length == 0 ? name : walked + "." + name;
            i = next < 0 ? path.Length : path[next] == '.' ? next + 1 : next;
        }

        return current;
    }

    private static string Display(string walked) => walked.Length == 0 ? "(root)" : walked;
}
