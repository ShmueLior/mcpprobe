using System.ComponentModel;
using ModelContextProtocol.Server;

namespace WeatherServer;

[McpServerResourceType]
public sealed class WeatherResources
{
    [McpServerResource(UriTemplate = "weather://cities", Name = "cities", MimeType = "text/plain")]
    [Description("The cities this server has forecasts for, one per line.")]
    public static string Cities() => string.Join("\n", WeatherData.Cities.Keys.Order(StringComparer.Ordinal));
}
