using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpProbe.Tests.TestSupport;

public sealed class GreetingService
{
    private readonly string _greeting = "Hello";

    public string Greet(string name) => $"{_greeting}, {name}!";
}

[McpServerToolType]
public sealed partial class TestTools(GreetingService greetings, ILogger<TestTools> logger)
{
    [McpServerTool(Name = "greet")]
    [Description("Greets a person by name, using a service from dependency injection.")]
    public string Greet([Description("The person's name.")] string name)
    {
        LogGreeting(logger, name);
        return greetings.Greet(name);
    }

    [McpServerTool(Name = "get_image")]
    [Description("Returns a one-pixel PNG image, for content-type tests.")]
    public static ImageContentBlock GetImage() =>
        ImageContentBlock.FromBytes(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII="), "image/png");

    [McpServerTool(Name = "get_text_and_data")]
    [Description("Returns a text block and a data content block.")]
    public static IEnumerable<AIContent> GetTextAndData() =>
        [new TextContent("first"), new TextContent("second")];

    [LoggerMessage(Level = LogLevel.Information, Message = "Greeting {Name}")]
    private static partial void LogGreeting(ILogger logger, string name);
}
