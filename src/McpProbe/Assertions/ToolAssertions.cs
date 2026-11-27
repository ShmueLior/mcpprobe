using System.Text.Json.Nodes;
using McpProbe.Internal;
using ModelContextProtocol.Protocol;

namespace McpProbe.Assertions;

/// <summary>Assertions for one tool's definition.</summary>
public sealed class ToolAssertions
{
    private readonly JsonNode? _inputSchema;

    internal ToolAssertions(Tool tool)
    {
        Tool = tool;
        _inputSchema = JsonSchemaReader.FromElement(tool.InputSchema);
    }

    /// <summary>Gets the tool under test.</summary>
    public Tool Tool { get; }

    /// <summary>Asserts the tool has a non-blank description, optionally containing some text.</summary>
    /// <param name="containing">Text the description must contain (ordinal), or <see langword="null"/> to only require a description.</param>
    /// <returns>The same assertions, for chaining.</returns>
    public ToolAssertions HasDescription(string? containing = null)
    {
        if (string.IsNullOrWhiteSpace(Tool.Description))
        {
            throw Fail.With($"Expected tool '{Tool.Name}' to have a description, but it has none.");
        }

        if (containing is not null && !Tool.Description.Contains(containing, StringComparison.Ordinal))
        {
            throw Fail.With($"Expected the description of tool '{Tool.Name}' to contain {Fail.Quote(containing)}, but it is {Fail.Quote(Tool.Description)}.");
        }

        return this;
    }

    /// <summary>Asserts the tool has a parameter, optionally of a JSON type and with a required flag.</summary>
    /// <param name="name">The parameter name (case-sensitive).</param>
    /// <param name="type">The JSON Schema type, for example <c>string</c> or <c>integer</c>. When the schema lists several types, this one must be among them.</param>
    /// <param name="required">Whether the parameter must be required (<see langword="true"/>) or optional (<see langword="false"/>); <see langword="null"/> to not check.</param>
    /// <returns>The same assertions, for chaining.</returns>
    public ToolAssertions HasParameter(string name, string? type = null, bool? required = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var properties = JsonSchemaReader.GetProperties(_inputSchema);
        if (properties is null || !properties.TryGetPropertyValue(name, out var property))
        {
            var names = properties?.Select(p => p.Key) ?? [];
            throw Fail.With($"Expected tool '{Tool.Name}' to have a parameter '{name}', but it doesn't. Parameters: {Fail.List(names)}.");
        }

        if (type is not null)
        {
            var types = JsonSchemaReader.GetTypes(property);
            if (types is null || !types.Contains(type))
            {
                throw Fail.With($"Expected parameter '{name}' of tool '{Tool.Name}' to be of type '{type}', but it is {JsonSchemaReader.FormatTypes(types)}.");
            }
        }

        if (required is { } expectRequired)
        {
            var isRequired = JsonSchemaReader.GetRequired(_inputSchema).Contains(name);
            if (isRequired != expectRequired)
            {
                throw Fail.With($"Expected parameter '{name}' of tool '{Tool.Name}' to be {(expectRequired ? "required" : "optional")}, but it is {(isRequired ? "required" : "optional")}.");
            }
        }

        return this;
    }

    /// <summary>Asserts the tool has no parameter with this name.</summary>
    /// <param name="name">The parameter name (case-sensitive).</param>
    /// <returns>The same assertions, for chaining.</returns>
    public ToolAssertions DoesNotHaveParameter(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (JsonSchemaReader.GetProperties(_inputSchema)?.ContainsKey(name) == true)
        {
            throw Fail.With($"Expected tool '{Tool.Name}' to have no parameter '{name}', but it has one.");
        }

        return this;
    }

    /// <summary>Asserts the tool declares an output schema (structured output).</summary>
    /// <returns>The same assertions, for chaining.</returns>
    public ToolAssertions HasOutputSchema()
    {
        if (Tool.OutputSchema is null)
        {
            throw Fail.With($"Expected tool '{Tool.Name}' to declare an output schema, but it doesn't.");
        }

        return this;
    }

    /// <summary>Asserts the tool's <c>readOnlyHint</c> annotation.</summary>
    /// <param name="expected">The expected value. An unset hint counts as <see langword="false"/>.</param>
    /// <returns>The same assertions, for chaining.</returns>
    public ToolAssertions HasReadOnlyHint(bool expected = true)
    {
        var actual = Tool.Annotations?.ReadOnlyHint ?? false;
        if (actual != expected)
        {
            throw Fail.With($"Expected tool '{Tool.Name}' to have readOnlyHint {(expected ? "true" : "false")}, but it is {(Tool.Annotations?.ReadOnlyHint is null ? "unset" : actual ? "true" : "false")}.");
        }

        return this;
    }

    /// <summary>Asserts the tool's <c>destructiveHint</c> annotation.</summary>
    /// <param name="expected">The expected value. An unset hint counts as <see langword="true"/>, as the MCP specification defines.</param>
    /// <returns>The same assertions, for chaining.</returns>
    public ToolAssertions HasDestructiveHint(bool expected = true)
    {
        var actual = Tool.Annotations?.DestructiveHint ?? true;
        if (actual != expected)
        {
            throw Fail.With($"Expected tool '{Tool.Name}' to have destructiveHint {(expected ? "true" : "false")}, but it is {(Tool.Annotations?.DestructiveHint is null ? "unset (defaults to true)" : actual ? "true" : "false")}.");
        }

        return this;
    }
}
