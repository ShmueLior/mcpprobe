using McpProbe.Assertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpProbe;

/// <summary>
/// Entry point for McpProbe assertions. Failures throw <see cref="McpProbeAssertionException"/>, so they work with
/// xUnit, NUnit, MSTest, TUnit or any other test framework.
/// </summary>
/// <example>
/// <code>
/// McpAssert.That(await client.ListToolsAsync()).ContainsTool("get_forecast").HasParameter("city", "string", required: true);
/// McpAssert.That(await client.CallToolAsync("get_forecast", args)).Succeeded().HasTextContent("Paris");
/// </code>
/// </example>
public static class McpAssert
{
    /// <summary>Starts assertions on a tool list.</summary>
    /// <param name="tools">The tools, for example from <c>ListToolsAsync</c>.</param>
    /// <returns>Assertions for the list.</returns>
    public static ToolListAssertions That(IEnumerable<McpClientTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        return new ToolListAssertions(tools.Select(t => t.ProtocolTool).ToList());
    }

    /// <summary>Starts assertions on a protocol tool list.</summary>
    /// <param name="tools">The tools.</param>
    /// <returns>Assertions for the list.</returns>
    public static ToolListAssertions That(IEnumerable<Tool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        return new ToolListAssertions(tools.ToList());
    }

    /// <summary>Starts assertions on one tool.</summary>
    /// <param name="tool">The tool.</param>
    /// <returns>Assertions for the tool.</returns>
    public static ToolAssertions That(McpClientTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return new ToolAssertions(tool.ProtocolTool);
    }

    /// <summary>Starts assertions on a tool call result.</summary>
    /// <param name="result">The result of <c>CallToolAsync</c>.</param>
    /// <returns>Assertions for the result.</returns>
    public static CallToolResultAssertions That(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new CallToolResultAssertions(result);
    }

    /// <summary>Starts assertions on a prompt list.</summary>
    /// <param name="prompts">The prompts, for example from <c>ListPromptsAsync</c>.</param>
    /// <returns>Assertions for the list.</returns>
    public static PromptListAssertions That(IEnumerable<McpClientPrompt> prompts)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        return new PromptListAssertions(prompts.Select(p => p.ProtocolPrompt).ToList());
    }

    /// <summary>Starts assertions on a prompt result.</summary>
    /// <param name="result">The result of <c>GetPromptAsync</c>.</param>
    /// <returns>Assertions for the result.</returns>
    public static GetPromptResultAssertions That(GetPromptResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new GetPromptResultAssertions(result);
    }

    /// <summary>Starts assertions on a resource list.</summary>
    /// <param name="resources">The resources, for example from <c>ListResourcesAsync</c>.</param>
    /// <returns>Assertions for the list.</returns>
    public static ResourceListAssertions That(IEnumerable<McpClientResource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return new ResourceListAssertions(resources.Select(r => r.ProtocolResource).ToList());
    }

    /// <summary>Starts assertions on a resource read result.</summary>
    /// <param name="result">The result of <c>ReadResourceAsync</c>.</param>
    /// <returns>Assertions for the result.</returns>
    public static ReadResourceResultAssertions That(ReadResourceResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ReadResourceResultAssertions(result);
    }
}
