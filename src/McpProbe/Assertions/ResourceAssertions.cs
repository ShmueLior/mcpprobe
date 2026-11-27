using McpProbe.Internal;
using ModelContextProtocol.Protocol;

namespace McpProbe.Assertions;

/// <summary>Assertions for a list of resources.</summary>
public sealed class ResourceListAssertions
{
    internal ResourceListAssertions(IReadOnlyList<Resource> resources) => Resources = resources;

    /// <summary>Gets the resources under test.</summary>
    public IReadOnlyList<Resource> Resources { get; }

    /// <summary>Asserts a resource with this exact URI exists.</summary>
    /// <param name="uri">The resource URI.</param>
    /// <returns>Assertions for that resource.</returns>
    public ResourceAssertions ContainsResource(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var resource = Resources.FirstOrDefault(r => string.Equals(r.Uri, uri, StringComparison.Ordinal))
            ?? throw Fail.With($"Expected a resource with URI '{uri}', but there is none. Resources: {Fail.List(Resources.Select(r => r.Uri))}.");
        return new ResourceAssertions(resource);
    }
}

/// <summary>Assertions for one resource's definition.</summary>
public sealed class ResourceAssertions
{
    internal ResourceAssertions(Resource resource) => Resource = resource;

    /// <summary>Gets the resource under test.</summary>
    public Resource Resource { get; }

    /// <summary>Asserts the resource name.</summary>
    /// <param name="expected">The expected name (case-sensitive).</param>
    /// <returns>The same assertions, for chaining.</returns>
    public ResourceAssertions HasName(string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (!string.Equals(Resource.Name, expected, StringComparison.Ordinal))
        {
            throw Fail.With($"Expected resource '{Resource.Uri}' to be named '{expected}', but it is named '{Resource.Name}'.");
        }

        return this;
    }

    /// <summary>Asserts the resource MIME type.</summary>
    /// <param name="expected">The expected MIME type (case-insensitive).</param>
    /// <returns>The same assertions, for chaining.</returns>
    public ResourceAssertions HasMimeType(string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (!string.Equals(Resource.MimeType, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw Fail.With($"Expected resource '{Resource.Uri}' to have MIME type '{expected}', but it is {Fail.Quote(Resource.MimeType)}.");
        }

        return this;
    }

    /// <summary>Asserts the resource has a non-blank description.</summary>
    /// <returns>The same assertions, for chaining.</returns>
    public ResourceAssertions HasDescription()
    {
        if (string.IsNullOrWhiteSpace(Resource.Description))
        {
            throw Fail.With($"Expected resource '{Resource.Uri}' to have a description, but it has none.");
        }

        return this;
    }
}

/// <summary>Assertions for the result of reading a resource.</summary>
public sealed class ReadResourceResultAssertions
{
    internal ReadResourceResultAssertions(ReadResourceResult result) => Result = result;

    /// <summary>Gets the result under test.</summary>
    public ReadResourceResult Result { get; }

    /// <summary>Asserts some text contents exist, optionally containing a value.</summary>
    /// <param name="containing">The text to find (ordinal), or <see langword="null"/> to only require text contents.</param>
    /// <returns>The same assertions, for chaining.</returns>
    public ReadResourceResultAssertions HasTextContent(string? containing = null)
    {
        var texts = Result.Contents.OfType<TextResourceContents>().Select(c => c.Text).ToList();
        if (texts.Count == 0)
        {
            throw Fail.With($"Expected text resource contents, but found {Result.Contents.Count} non-text item(s).");
        }

        if (containing is not null && !texts.Any(t => t.Contains(containing, StringComparison.Ordinal)))
        {
            throw Fail.With($"Expected resource text containing {Fail.Quote(containing)}, but it was {Fail.Quote(string.Join("\n", texts))}.");
        }

        return this;
    }

    /// <summary>Asserts every content item has this MIME type.</summary>
    /// <param name="expected">The expected MIME type (case-insensitive).</param>
    /// <returns>The same assertions, for chaining.</returns>
    public ReadResourceResultAssertions HasMimeType(string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var mismatched = Result.Contents.Where(c => !string.Equals(c.MimeType, expected, StringComparison.OrdinalIgnoreCase)).ToList();
        if (Result.Contents.Count == 0 || mismatched.Count > 0)
        {
            throw Fail.With($"Expected resource contents with MIME type '{expected}', but found {Fail.List(Result.Contents.Select(c => c.MimeType ?? "(none)"))}.");
        }

        return this;
    }
}
