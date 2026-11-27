using System.Globalization;
using System.Text.Json;
using McpProbe.Internal;
using ModelContextProtocol.Protocol;

namespace McpProbe.Assertions;

/// <summary>Assertions for the result of a tool call.</summary>
public sealed class CallToolResultAssertions
{
    internal CallToolResultAssertions(CallToolResult result) => Result = result;

    /// <summary>Gets the result under test.</summary>
    public CallToolResult Result { get; }

    /// <summary>Asserts the call succeeded (<c>isError</c> is not <see langword="true"/>).</summary>
    /// <returns>The same assertions, for chaining.</returns>
    public CallToolResultAssertions Succeeded()
    {
        if (Result.IsError == true)
        {
            throw Fail.With($"Expected the tool call to succeed, but it returned an error: {Fail.Quote(AllText())}.");
        }

        return this;
    }

    /// <summary>Asserts the call returned a tool error (<c>isError: true</c>), optionally with text in the error content.</summary>
    /// <param name="messageContaining">Text the error content must contain (ordinal), or <see langword="null"/> to not check.</param>
    /// <returns>The same assertions, for chaining.</returns>
    public CallToolResultAssertions Failed(string? messageContaining = null)
    {
        if (Result.IsError != true)
        {
            throw Fail.With($"Expected the tool call to return an error, but it succeeded with: {Fail.Quote(AllText())}.");
        }

        if (messageContaining is not null && !AllText().Contains(messageContaining, StringComparison.Ordinal))
        {
            throw Fail.With($"Expected the tool error to contain {Fail.Quote(messageContaining)}, but it was {Fail.Quote(AllText())}.");
        }

        return this;
    }

    /// <summary>Asserts at least one text block exists, optionally containing some text.</summary>
    /// <param name="containing">Text one block must contain, or <see langword="null"/> to only require a text block.</param>
    /// <param name="comparison">How to compare. Defaults to ordinal.</param>
    /// <returns>The same assertions, for chaining.</returns>
    public CallToolResultAssertions HasTextContent(string? containing = null, StringComparison comparison = StringComparison.Ordinal)
    {
        var texts = Result.Content.OfType<TextContentBlock>().Select(b => b.Text).ToList();
        if (texts.Count == 0)
        {
            throw Fail.With($"Expected a text content block, but the result has {DescribeContent()}.");
        }

        if (containing is not null && !texts.Any(t => t.Contains(containing, comparison)))
        {
            throw Fail.With($"Expected a text content block containing {Fail.Quote(containing)}, but the text was {Fail.Quote(AllText())}.");
        }

        return this;
    }

    /// <summary>Asserts at least one content block of a type exists, optionally matching a predicate.</summary>
    /// <typeparam name="TBlock">The block type, for example <see cref="ImageContentBlock"/> or <see cref="EmbeddedResourceBlock"/>.</typeparam>
    /// <param name="predicate">An extra condition, or <see langword="null"/>.</param>
    /// <returns>The same assertions, for chaining.</returns>
    public CallToolResultAssertions HasContent<TBlock>(Func<TBlock, bool>? predicate = null)
        where TBlock : ContentBlock
    {
        var blocks = Result.Content.OfType<TBlock>().ToList();
        if (blocks.Count == 0)
        {
            throw Fail.With($"Expected a {typeof(TBlock).Name}, but the result has {DescribeContent()}.");
        }

        if (predicate is not null && !blocks.Any(predicate))
        {
            throw Fail.With($"Expected a {typeof(TBlock).Name} matching the predicate, but none of the {blocks.Count} matched.");
        }

        return this;
    }

    /// <summary>Asserts at least one image block exists, optionally with a MIME type.</summary>
    /// <param name="mimeType">The expected MIME type, for example <c>image/png</c>, or <see langword="null"/>.</param>
    /// <returns>The same assertions, for chaining.</returns>
    public CallToolResultAssertions HasImageContent(string? mimeType = null)
    {
        var images = Result.Content.OfType<ImageContentBlock>().ToList();
        if (images.Count == 0)
        {
            throw Fail.With($"Expected an image content block, but the result has {DescribeContent()}.");
        }

        if (mimeType is not null && !images.Any(i => string.Equals(i.MimeType, mimeType, StringComparison.OrdinalIgnoreCase)))
        {
            throw Fail.With($"Expected an image of type '{mimeType}', but found {Fail.List(images.Select(i => i.MimeType))}.");
        }

        return this;
    }

    /// <summary>Asserts the result has structured content.</summary>
    /// <returns>The same assertions, for chaining.</returns>
    public CallToolResultAssertions HasStructuredContent()
    {
        if (Result.StructuredContent is null || Result.StructuredContent.Value.ValueKind == JsonValueKind.Undefined)
        {
            throw Fail.With("Expected structured content, but the result has none. Is UseStructuredContent set on the tool?");
        }

        return this;
    }

    /// <summary>Asserts a string value in the structured content.</summary>
    /// <param name="path">A path such as <c>city</c>, <c>current.temperature</c> or <c>days[0].summary</c>.</param>
    /// <param name="expected">The expected string, or <see langword="null"/> for JSON <c>null</c>.</param>
    /// <returns>The same assertions, for chaining.</returns>
    public CallToolResultAssertions HasStructuredValue(string path, string? expected)
    {
        var element = Navigate(path);
        var matches = expected is null
            ? element.ValueKind == JsonValueKind.Null
            : element.ValueKind == JsonValueKind.String && element.GetString() == expected;
        return matches ? this : throw Mismatch(path, expected is null ? "null" : Fail.Quote(expected), element);
    }

    /// <summary>Asserts an integer value in the structured content.</summary>
    /// <param name="path">A path such as <c>count</c> or <c>days[0].high</c>.</param>
    /// <param name="expected">The expected value.</param>
    /// <returns>The same assertions, for chaining.</returns>
    public CallToolResultAssertions HasStructuredValue(string path, long expected)
    {
        var element = Navigate(path);
        var matches = element.ValueKind == JsonValueKind.Number &&
            (element.TryGetInt64(out var l) ? l == expected : element.TryGetDecimal(out var d) && d == expected);
        return matches ? this : throw Mismatch(path, expected.ToString(CultureInfo.InvariantCulture), element);
    }

    /// <summary>Asserts a number in the structured content, within a tolerance.</summary>
    /// <param name="path">A path such as <c>temperature</c>.</param>
    /// <param name="expected">The expected value.</param>
    /// <param name="tolerance">The allowed absolute difference. Defaults to 0 (exact).</param>
    /// <returns>The same assertions, for chaining.</returns>
    public CallToolResultAssertions HasStructuredValue(string path, double expected, double tolerance = 0)
    {
        var element = Navigate(path);
        var matches = element.ValueKind == JsonValueKind.Number && Math.Abs(element.GetDouble() - expected) <= tolerance;
        return matches ? this : throw Mismatch(path, expected.ToString("R", CultureInfo.InvariantCulture), element);
    }

    /// <summary>Asserts a boolean in the structured content.</summary>
    /// <param name="path">A path such as <c>isSevere</c>.</param>
    /// <param name="expected">The expected value.</param>
    /// <returns>The same assertions, for chaining.</returns>
    public CallToolResultAssertions HasStructuredValue(string path, bool expected)
    {
        var element = Navigate(path);
        var matches = element.ValueKind == (expected ? JsonValueKind.True : JsonValueKind.False);
        return matches ? this : throw Mismatch(path, expected ? "true" : "false", element);
    }

    private JsonElement Navigate(string path)
    {
        HasStructuredContent();
        return StructuredPath.Navigate(Result.StructuredContent!.Value, path);
    }

    private static McpProbeAssertionException Mismatch(string path, string expected, JsonElement actual) =>
        Fail.With($"Expected structured content '{path}' to be {expected}, but it is {Fail.Truncate(actual.GetRawText(), 200)}.");

    private string AllText() => string.Join("\n", Result.Content.OfType<TextContentBlock>().Select(b => b.Text));

    private string DescribeContent() =>
        Result.Content.Count == 0 ? "no content blocks" : $"content blocks of type {Fail.List(Result.Content.Select(c => c.Type))}";
}
