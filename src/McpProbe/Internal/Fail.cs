namespace McpProbe.Internal;

internal static class Fail
{
    private const int MaxListedNames = 20;

    public static McpProbeAssertionException With(string message) => new(message);

    /// <summary>Quotes server-supplied text for a failure message.</summary>
    public static string Quote(string? value) => value is null ? "(none)" : $"\"{Printable.Text(value)}\"";

    /// <summary>Lists server-supplied names for a failure message, at most 20 of them.</summary>
    public static string List(IEnumerable<string> names)
    {
        var list = names.ToList();
        if (list.Count == 0)
        {
            return "(none)";
        }

        var shown = string.Join(", ", list.Take(MaxListedNames).Select(n => $"'{Printable.Text(n, 100)}'"));
        return list.Count <= MaxListedNames ? shown : $"{shown} and {list.Count - MaxListedNames} more";
    }

    public static string Truncate(string value, int max) => Printable.Text(value, max);
}
