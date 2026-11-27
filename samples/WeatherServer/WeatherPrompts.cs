using System.ComponentModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace WeatherServer;

[McpServerPromptType]
public sealed class WeatherPrompts
{
    [McpServerPrompt(Name = "plan_trip")]
    [Description("Asks the model to plan what to pack for a trip, based on the forecast.")]
    public static ChatMessage PlanTrip(
        [Description("Destination city.")] string city,
        [Description("Trip length in days.")] int days = 3) =>
        new(ChatRole.User, $"I'm going to {city} for {days} days. Check the forecast with get_forecast and tell me what to pack.");
}
