using System.Globalization;
using McpProbe.Internal;
using ModelContextProtocol.Protocol;

namespace McpProbe.Assertions;

/// <summary>Assertions for a list of prompts.</summary>
public sealed class PromptListAssertions
{
    internal PromptListAssertions(IReadOnlyList<Prompt> prompts) => Prompts = prompts;

    /// <summary>Gets the prompts under test.</summary>
    public IReadOnlyList<Prompt> Prompts { get; }

    /// <summary>Asserts a prompt with this exact name exists.</summary>
    /// <param name="name">The prompt name (case-sensitive).</param>
    /// <returns>Assertions for that prompt.</returns>
    public PromptAssertions ContainsPrompt(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var prompt = Prompts.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal))
            ?? throw Fail.With($"Expected a prompt named '{name}', but there is none. Prompts: {Fail.List(Prompts.Select(p => p.Name))}.");
        return new PromptAssertions(prompt);
    }
}

/// <summary>Assertions for one prompt's definition.</summary>
public sealed class PromptAssertions
{
    internal PromptAssertions(Prompt prompt) => Prompt = prompt;

    /// <summary>Gets the prompt under test.</summary>
    public Prompt Prompt { get; }

    /// <summary>Asserts the prompt has a non-blank description, optionally containing some text.</summary>
    /// <param name="containing">Text the description must contain (ordinal), or <see langword="null"/>.</param>
    /// <returns>The same assertions, for chaining.</returns>
    public PromptAssertions HasDescription(string? containing = null)
    {
        if (string.IsNullOrWhiteSpace(Prompt.Description))
        {
            throw Fail.With($"Expected prompt '{Prompt.Name}' to have a description, but it has none.");
        }

        if (containing is not null && !Prompt.Description.Contains(containing, StringComparison.Ordinal))
        {
            throw Fail.With($"Expected the description of prompt '{Prompt.Name}' to contain {Fail.Quote(containing)}, but it is {Fail.Quote(Prompt.Description)}.");
        }

        return this;
    }

    /// <summary>Asserts the prompt has an argument, optionally checking whether it is required.</summary>
    /// <param name="name">The argument name (case-sensitive).</param>
    /// <param name="required">The expected required flag, or <see langword="null"/> to not check.</param>
    /// <returns>The same assertions, for chaining.</returns>
    public PromptAssertions HasArgument(string name, bool? required = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var arguments = Prompt.Arguments ?? [];
        var argument = arguments.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal))
            ?? throw Fail.With($"Expected prompt '{Prompt.Name}' to have an argument '{name}', but it doesn't. Arguments: {Fail.List(arguments.Select(a => a.Name))}.");

        if (required is { } expected && (argument.Required ?? false) != expected)
        {
            throw Fail.With($"Expected argument '{name}' of prompt '{Prompt.Name}' to be {(expected ? "required" : "optional")}, but it is {(argument.Required == true ? "required" : "optional")}.");
        }

        return this;
    }
}

/// <summary>Assertions for the result of getting a prompt.</summary>
public sealed class GetPromptResultAssertions
{
    internal GetPromptResultAssertions(GetPromptResult result) => Result = result;

    /// <summary>Gets the result under test.</summary>
    public GetPromptResult Result { get; }

    /// <summary>Asserts the number of messages.</summary>
    /// <param name="expected">The expected count.</param>
    /// <returns>The same assertions, for chaining.</returns>
    public GetPromptResultAssertions HasMessageCount(int expected)
    {
        if (Result.Messages.Count != expected)
        {
            throw Fail.With(string.Create(CultureInfo.InvariantCulture, $"Expected {expected} prompt message(s), but found {Result.Messages.Count}."));
        }

        return this;
    }

    /// <summary>Asserts some message has text containing a value.</summary>
    /// <param name="containing">The text to find (ordinal).</param>
    /// <param name="role">The role the message must have, or <see langword="null"/> for any.</param>
    /// <returns>The same assertions, for chaining.</returns>
    public GetPromptResultAssertions HasTextContent(string containing, Role? role = null)
    {
        ArgumentNullException.ThrowIfNull(containing);
        var texts = Result.Messages
            .Where(m => role is null || m.Role == role)
            .Select(m => m.Content)
            .OfType<TextContentBlock>()
            .Select(b => b.Text)
            .ToList();
        if (!texts.Any(t => t.Contains(containing, StringComparison.Ordinal)))
        {
            throw Fail.With($"Expected a prompt message{(role is null ? string.Empty : $" with role {role}")} containing {Fail.Quote(containing)}, but the text was {Fail.Quote(string.Join("\n", texts))}.");
        }

        return this;
    }
}
