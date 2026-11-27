namespace McpProbe.Linting;

internal enum NamingStyle
{
    /// <summary>A single lowercase word (<c>search</c>). Fits snake_case, kebab-case and camelCase.</summary>
    Lower,
    SnakeCase,
    KebabCase,
    CamelCase,
    PascalCase,
    Other,
}

internal static class NamingStyles
{
    public static NamingStyle Detect(string name)
    {
        if (name.Length == 0)
        {
            return NamingStyle.Other;
        }

        var hasUnderscore = name.Contains('_', StringComparison.Ordinal);
        var hasHyphen = name.Contains('-', StringComparison.Ordinal);
        var hasUpper = name.Any(char.IsUpper);
        var allLowerOrDigit = name.All(c => char.IsLower(c) || char.IsDigit(c));

        if (allLowerOrDigit && char.IsLetter(name[0]))
        {
            return NamingStyle.Lower;
        }

        if (hasUnderscore && !hasHyphen && !hasUpper && IsSeparated(name, '_'))
        {
            return NamingStyle.SnakeCase;
        }

        if (hasHyphen && !hasUnderscore && !hasUpper && IsSeparated(name, '-'))
        {
            return NamingStyle.KebabCase;
        }

        if (!hasUnderscore && !hasHyphen && name.All(char.IsLetterOrDigit))
        {
            return char.IsLower(name[0]) ? NamingStyle.CamelCase : char.IsUpper(name[0]) ? NamingStyle.PascalCase : NamingStyle.Other;
        }

        return NamingStyle.Other;
    }

    public static string Label(NamingStyle style) => style switch
    {
        NamingStyle.Lower => "lowercase",
        NamingStyle.SnakeCase => "snake_case",
        NamingStyle.KebabCase => "kebab-case",
        NamingStyle.CamelCase => "camelCase",
        NamingStyle.PascalCase => "PascalCase",
        _ => "mixed",
    };

    private static bool IsSeparated(string name, char separator)
    {
        var parts = name.Split(separator);
        return parts.All(p => p.Length > 0 && p.All(c => char.IsLower(c) || char.IsDigit(c)));
    }
}
