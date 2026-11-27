namespace WeatherServer;

/// <summary>Fixed, made-up data so the sample is deterministic and needs no network.</summary>
internal static class WeatherData
{
    public static readonly IReadOnlyDictionary<string, (int High, int Low, string Summary)> Cities =
        new Dictionary<string, (int, int, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["London"] = (14, 8, "Light rain"),
            ["Paris"] = (18, 10, "Partly cloudy"),
            ["Tel Aviv"] = (27, 19, "Sunny"),
            ["Tokyo"] = (21, 14, "Clear"),
        };
}
