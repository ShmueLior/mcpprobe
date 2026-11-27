using System.Text;

namespace McpProbe.Cli;

/// <summary>
/// Splits a command line like <c>dotnet run --project "My Server"</c> into a program and arguments.
/// Supports double and single quotes and backslash escapes inside double quotes. No variable expansion.
/// </summary>
internal static class CommandLineSplitter
{
    public static IReadOnlyList<string> Split(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        var result = new List<string>();
        var current = new StringBuilder();
        var inToken = false;
        char? quote = null;

        for (var i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];
            if (quote is null)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (inToken)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                        inToken = false;
                    }

                    continue;
                }

                if (c is '"' or '\'')
                {
                    quote = c;
                    inToken = true;
                    continue;
                }

                current.Append(c);
                inToken = true;
                continue;
            }

            if (c == quote)
            {
                quote = null;
                continue;
            }

            if (quote == '"' && c == '\\' && i + 1 < commandLine.Length && commandLine[i + 1] is '"' or '\\')
            {
                current.Append(commandLine[++i]);
                continue;
            }

            current.Append(c);
        }

        if (quote is not null)
        {
            throw new FormatException($"Unclosed {quote} quote in command line.");
        }

        if (inToken)
        {
            result.Add(current.ToString());
        }

        return result;
    }
}
