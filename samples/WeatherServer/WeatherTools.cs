using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace WeatherServer;

[McpServerToolType]
public sealed class WeatherTools
{
    [McpServerTool(Name = "get_forecast", ReadOnly = true, UseStructuredContent = true)]
    [Description("Gets a daily weather forecast for a city. Use it when the user asks about upcoming weather.")]
    public static Forecast GetForecast(
        [Description("City name, for example 'Paris' or 'Tel Aviv'.")] string city,
        [Description("Number of days to forecast, from 1 to 7.")] int days = 3)
    {
        if (days is < 1 or > 7)
        {
            throw new McpException("days must be between 1 and 7.");
        }

        var (high, low, summary) = Lookup(city);
        var forecastDays = Enumerable.Range(0, days)
            .Select(i => new ForecastDay(i + 1, high + (i % 3) - 1, low + (i % 2), summary))
            .ToList();
        return new Forecast(city, forecastDays);
    }

    [McpServerTool(Name = "get_alerts", ReadOnly = true)]
    [Description("Lists active weather alerts for a city. Returns 'No active alerts' when there are none.")]
    public static string GetAlerts(
        [Description("City name, for example 'London'.")] string city)
    {
        var (high, _, _) = Lookup(city);
        return high >= 25 ? $"Heat advisory for {city}: highs around {high}°C." : $"No active alerts for {city}.";
    }

    [McpServerTool(Name = "convert_temperature", ReadOnly = true, Idempotent = true)]
    [Description("Converts a temperature between Celsius and Fahrenheit.")]
    public static string ConvertTemperature(
        [Description("The temperature to convert.")] double degrees,
        [Description("The unit of the given temperature.")] TemperatureUnit from)
    {
        var converted = from == TemperatureUnit.Celsius ? (degrees * 9 / 5) + 32 : (degrees - 32) * 5 / 9;
        var to = from == TemperatureUnit.Celsius ? "°F" : "°C";
        return FormattableString.Invariant($"{converted:0.#}{to}");
    }

    private static (int High, int Low, string Summary) Lookup(string city) =>
        WeatherData.Cities.TryGetValue(city, out var data)
            ? data
            : throw new McpException($"Unknown city '{city}'. Known cities: {string.Join(", ", WeatherData.Cities.Keys)}.");
}

public enum TemperatureUnit
{
    Celsius,
    Fahrenheit,
}

public sealed record Forecast(
    [property: Description("The city the forecast is for.")] string City,
    [property: Description("One entry per day, starting tomorrow.")] IReadOnlyList<ForecastDay> Days);

public sealed record ForecastDay(
    [property: Description("Days from today, starting at 1.")] int Day,
    [property: Description("High temperature in °C.")] int High,
    [property: Description("Low temperature in °C.")] int Low,
    [property: Description("Short summary, for example 'Sunny'.")] string Summary);
