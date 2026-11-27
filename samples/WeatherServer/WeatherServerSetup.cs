using Microsoft.Extensions.DependencyInjection;

namespace WeatherServer;

public static class WeatherServerSetup
{
    /// <summary>
    /// Registers the weather tools, prompts and resources. Program.cs and the tests share this,
    /// so the tests exercise exactly what the real server exposes.
    /// </summary>
    public static IMcpServerBuilder WithWeatherFeatures(this IMcpServerBuilder builder) =>
        builder
            .WithTools<WeatherTools>()
            .WithPrompts<WeatherPrompts>()
            .WithResources<WeatherResources>();
}
