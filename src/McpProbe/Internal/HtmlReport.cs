using System.Net;
using System.Text;

namespace McpProbe.Internal;

/// <summary>
/// Builds the self-contained HTML for lint and diff reports. Callers pass cells already run through
/// <see cref="Printable"/>; every cell, title and summary is HTML-encoded here, and only as element text.
/// </summary>
internal static class HtmlReport
{
    private const string Css =
        "body{font-family:system-ui,sans-serif;margin:2rem;background:#fff;color:#1f2328}" +
        ".banner{display:inline-block;padding:.25rem .75rem;border-radius:.25rem;font-weight:600;color:#fff}" +
        ".pass{background:#1a7f37}.fail{background:#cf222e}.note{background:#9a6700}" +
        "table{border-collapse:collapse;margin-top:1rem}th,td{border:1px solid #d0d7de;padding:.35rem .6rem;text-align:left;vertical-align:top}" +
        "th{background:#f6f8fa}td{overflow-wrap:anywhere}th:nth-child(1),th:nth-child(2),th:nth-child(6),td:nth-child(1),td:nth-child(2),td:nth-child(6){white-space:nowrap}.error td:first-child,.breaking td:first-child{color:#cf222e;font-weight:600}" +
        ".warning td:first-child,.non-breaking td:first-child{color:#9a6700;font-weight:600}";

    /// <param name="title">The page title and heading.</param>
    /// <param name="bannerClass">One of <c>pass</c>, <c>fail</c> or <c>note</c>; fixed values, never server text.</param>
    /// <param name="banner">The banner text.</param>
    /// <param name="summary">The one-line summary.</param>
    /// <param name="headers">The table headers.</param>
    /// <param name="rows">The rows: a fixed CSS class (never server text) and the cells.</param>
    public static string Page(string title, string bannerClass, string banner, string summary, string[] headers, IEnumerable<(string RowClass, string[] Cells)> rows)
    {
        var sb = new StringBuilder("<!DOCTYPE html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<title>").Append(Encode(title)).Append("</title><style>").Append(Css).Append("</style></head><body>\n");
        sb.Append("<h1>").Append(Encode(title)).Append("</h1>\n");
        sb.Append("<p><span class=\"banner ").Append(bannerClass).Append("\">").Append(Encode(banner)).Append("</span></p>\n");
        sb.Append("<p>").Append(Encode(summary)).Append("</p>\n");

        var any = false;
        foreach (var (rowClass, cells) in rows)
        {
            if (!any)
            {
                any = true;
                sb.Append("<table><thead><tr>");
                foreach (var header in headers)
                {
                    sb.Append("<th>").Append(Encode(header)).Append("</th>");
                }

                sb.Append("</tr></thead><tbody>\n");
            }

            sb.Append("<tr class=\"").Append(rowClass).Append("\">");
            foreach (var cell in cells)
            {
                sb.Append("<td>").Append(Encode(cell)).Append("</td>");
            }

            sb.Append("</tr>\n");
        }

        if (any)
        {
            sb.Append("</tbody></table>\n");
        }

        return sb.Append("</body></html>\n").ToString();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
