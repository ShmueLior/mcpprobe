using System.Globalization;
using System.Text;

namespace McpProbe.Internal;

/// <summary>
/// Makes server-supplied text (tool names, parameter names, enum values) safe to put in a message or a CI log:
/// control and format characters (including bidi overrides) become <c>\uXXXX</c>, backslash becomes <c>\\</c>,
/// (unless <c>escapeBackslash</c> is false, for messages with file paths), and the result is capped at <c>maxLength</c> characters without splitting an escape or a surrogate pair.
/// </summary>
internal static class Printable
{
    public const int DefaultMaxLength = 200;

    public static string Text(string value, int maxLength = DefaultMaxLength, bool escapeBackslash = true)
    {
        if (IsClean(value, maxLength, escapeBackslash))
        {
            return value;
        }

        const string Ellipsis = "...";
        var fits = EscapedLengthAtMost(value, maxLength, escapeBackslash);
        var budget = fits ? maxLength : Math.Max(maxLength - Ellipsis.Length, 0);
        var sb = new StringBuilder(Math.Min(maxLength, value.Length * 2));
        for (var i = 0; i < value.Length; i++)
        {
            var isPair = char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]);
            var piece = isPair ? value.Substring(i, 2) : Escape(value[i], escapeBackslash);
            if (sb.Length + piece.Length > budget)
            {
                break;
            }

            sb.Append(piece);
            i += isPair ? 1 : 0;
        }

        return fits ? sb.ToString() : sb.Append(Ellipsis).ToString();
    }

    /// <summary>Measures the escaped length, stopping as soon as it passes <paramref name="maxLength"/>.</summary>
    private static bool EscapedLengthAtMost(string value, int maxLength, bool escapeBackslash)
    {
        var length = 0;
        for (var i = 0; i < value.Length && length <= maxLength; i++)
        {
            var isPair = char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]);
            length += isPair ? 2 : Escape(value[i], escapeBackslash).Length;
            i += isPair ? 1 : 0;
        }

        return length <= maxLength;
    }

    private static bool IsClean(string value, int maxLength, bool escapeBackslash)
    {
        if (value.Length > maxLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (NeedsEscape(c, escapeBackslash) || char.IsSurrogate(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool NeedsEscape(char c, bool escapeBackslash) =>
        (escapeBackslash && c == '\\') || char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    private static string Escape(char c, bool escapeBackslash) =>
        escapeBackslash && c == '\\' ? "\\\\" :
        NeedsEscape(c, escapeBackslash) || char.IsSurrogate(c) ? "\\u" + ((int)c).ToString("X4", CultureInfo.InvariantCulture) :
        c.ToString();
}
