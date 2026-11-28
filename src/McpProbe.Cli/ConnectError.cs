using System.Net;
using System.Text.RegularExpressions;
using McpProbe.Internal;
using ModelContextProtocol.Client;

namespace McpProbe.Cli;

/// <summary>
/// Turns the SDK's HTTP failure (status plus up to 1 KB of the response body, which may be a whole HTML page or advice
/// for the server author) into one short line that tells the CLI user what to try next.
/// </summary>
internal static partial class ConnectError
{
    private const string BodyMarker = "Response body: ";
    private const int PreviewLength = 200;

    public static string Describe(HttpRequestException ex, Uri endpoint, HttpTransportMode mode, bool sentAuthorization, string? wwwAuthenticate)
    {
        var status = ex.StatusCode ?? throw new ArgumentException("The exception has no status code.", nameof(ex));
        var code = (int)status;
        var url = endpoint.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.SafeUnescaped);

        if (status == HttpStatusCode.Unauthorized)
        {
            var challenge = string.IsNullOrWhiteSpace(wwwAuthenticate) ? "" : $" (WWW-Authenticate: {Printable.Text(wwwAuthenticate)})";
            return sentAuthorization
                ? $"401 Unauthorized from {url}. The credentials in --header were rejected{challenge}."
                : $"401 Unauthorized from {url}. The server wants authentication{challenge}. Pass --header 'Authorization: Bearer <token>'.";
        }

        if (status is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
        {
            if (mode == HttpTransportMode.Sse)
            {
                return $"The server did not answer as a legacy SSE endpoint at this URL (HTTP {code}). Legacy SSE usually lives at /sse; use --transport auto or streamable-http for Streamable HTTP.";
            }

            if (mode == HttpTransportMode.StreamableHttp)
            {
                return $"The server did not answer as Streamable HTTP at this URL (HTTP {code}). Streamable HTTP is usually at /mcp; use --transport auto or sse for a legacy SSE endpoint.";
            }
        }

        var preview = Preview(ex.Message);
        return preview.Length == 0 ? $"HTTP {code} from {url}." : $"HTTP {code} from {url}: \"{preview}\"";
    }

    /// <summary>The response body from an SDK exception message as one line: tags stripped when it is HTML, whitespace collapsed, at most 200 characters.</summary>
    internal static string Preview(string exceptionMessage)
    {
        var start = exceptionMessage.IndexOf(BodyMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return "";
        }

        var body = exceptionMessage[(start + BodyMarker.Length)..];
        if (body.TrimStart().StartsWith('<'))
        {
            body = WebUtility.HtmlDecode(Tags().Replace(body, " "));
        }

        return Printable.Text(Whitespace().Replace(body, " ").Trim(), PreviewLength);
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>Remembers the last <c>WWW-Authenticate</c> value the server sent. The body is left alone, the SDK reads it.</summary>
internal sealed class ChallengeCapture() : DelegatingHandler(new SocketsHttpHandler())
{
    private volatile string? _challenge;

    public string? Challenge => _challenge;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        _challenge = response.Headers.WwwAuthenticate is { Count: > 0 } values ? string.Join(", ", values) : null;
        return response;
    }
}
