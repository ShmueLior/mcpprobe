using System.Net;
using McpProbe.Cli;
using ModelContextProtocol.Client;

namespace McpProbe.Tests;

public sealed class ConnectErrorTests
{
    private static readonly Uri s_endpoint = new("https://user:pw@example.test/mcp?token=secret#frag");

    private static HttpRequestException Failure(HttpStatusCode status, string? body = null) =>
        new($"Response status code does not indicate success: {(int)status} ({status})." + (body is null ? "" : " Response body: " + body), null, status);

    [Theory]
    [InlineData("", "")]
    [InlineData("{\"error\": {\"code\": -32000}}", "{\"error\": {\"code\": -32000}}")]
    [InlineData("  line one\n\n   line two\t", "line one line two")]
    [InlineData("<html>\n  <body><h1>405: No</h1> <p>Use &lt;POST&gt; &amp; wait</p></body>\n</html>", "405: No Use <POST> & wait")]
    [InlineData("not html but 1 < 2 and 3 > 2", "not html but 1 < 2 and 3 > 2")]
    public void Preview_IsOneLineWithoutMarkup(string body, string expected) =>
        Assert.Equal(expected, ConnectError.Preview("Response status code does not indicate success: 405 (Method Not Allowed). Response body: " + body));

    [Fact]
    public void Preview_WithoutABody_IsEmpty() =>
        Assert.Equal("", ConnectError.Preview("Response status code does not indicate success: 405 (Method Not Allowed)."));

    [Fact]
    public void Preview_IsCappedAndNeverSplitsAnEscapeOrSurrogatePair()
    {
        var preview = ConnectError.Preview("Response body: " + new string('\u0007', 150) + "\U0001F600\U0001F600");

        Assert.True(preview.Length <= 200, $"{preview.Length}");
        Assert.EndsWith("...", preview, StringComparison.Ordinal);
        Assert.DoesNotContain('\u0007', preview);
        Assert.Matches(@"^(\\u0007)+\.\.\.$", preview);
    }

    [Fact]
    public void Describe_ShowsTheEndpointWithoutCredentialsOrQuery() =>
        Assert.Equal(
            "HTTP 500 from https://example.test/mcp: \"boom\"",
            ConnectError.Describe(Failure(HttpStatusCode.InternalServerError, "boom"), s_endpoint, HttpTransportMode.AutoDetect, false, null));

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, 400)]
    [InlineData(HttpStatusCode.NotFound, 404)]
    [InlineData(HttpStatusCode.MethodNotAllowed, 405)]
    public void Describe_PinnedTransportMismatch_NamesTheTransportAndCode(HttpStatusCode status, int code)
    {
        var sse = ConnectError.Describe(Failure(status, "advice for the server author"), s_endpoint, HttpTransportMode.Sse, false, null);
        var streamable = ConnectError.Describe(Failure(status, "advice for the server author"), s_endpoint, HttpTransportMode.StreamableHttp, false, null);

        Assert.StartsWith($"The server did not answer as a legacy SSE endpoint at this URL (HTTP {code}).", sse, StringComparison.Ordinal);
        Assert.StartsWith($"The server did not answer as Streamable HTTP at this URL (HTTP {code}).", streamable, StringComparison.Ordinal);
        Assert.DoesNotContain("advice", sse + streamable, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, HttpTransportMode.Sse)]
    [InlineData(HttpStatusCode.InternalServerError, HttpTransportMode.StreamableHttp)]
    [InlineData(HttpStatusCode.MethodNotAllowed, HttpTransportMode.AutoDetect)]
    public void Describe_OtherCases_HaveNoTransportHint(HttpStatusCode status, HttpTransportMode mode) =>
        Assert.StartsWith($"HTTP {(int)status} from https://example.test/mcp", ConnectError.Describe(Failure(status), s_endpoint, mode, false, null), StringComparison.Ordinal);

    [Fact]
    public void Describe_Unauthorized_EscapesTheServersChallenge()
    {
        var message = ConnectError.Describe(Failure(HttpStatusCode.Unauthorized), s_endpoint, HttpTransportMode.AutoDetect, false, "Bearer realm=\"x\u001b[31m\\\"");

        Assert.Contains("(WWW-Authenticate: Bearer realm=\"x\\u001B[31m\\\\\")", message, StringComparison.Ordinal);
        Assert.DoesNotContain('\u001b', message);
    }

    [Fact]
    public void Describe_Unauthorized_WithoutAChallengeLeavesThatPartOut() =>
        Assert.Equal(
            "401 Unauthorized from https://example.test/mcp. The server wants authentication. Pass --header 'Authorization: Bearer <token>'.",
            ConnectError.Describe(Failure(HttpStatusCode.Unauthorized), s_endpoint, HttpTransportMode.AutoDetect, false, " "));
}
