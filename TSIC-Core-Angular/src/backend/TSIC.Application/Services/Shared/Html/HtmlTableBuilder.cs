using System.Net;
using System.Text;

namespace TSIC.Application.Services.Shared.Html;

/// <summary>
/// Pure business logic for HTML table generation.
/// Provides dual-mode rendering for web UI (CSS classes) and email clients (inline styles).
/// </summary>
public static class HtmlTableBuilder
{
    /// <summary>
    /// Starts an HTML table with appropriate styling for the target mode.
    /// </summary>
    /// <param name="sb">The StringBuilder to append to.</param>
    /// <param name="emailMode">True for email-compatible inline styles, false for CSS classes.</param>
    public static void StartTable(StringBuilder sb, bool emailMode)
    {
        if (emailMode)
            sb.Append("<table border='1' cellpadding='4' cellspacing='0' style='border:1px solid #000;border-collapse:collapse;width:100%;font-family:Arial,Helvetica,sans-serif;font-size:12px;' role='table'>");
        else
            sb.Append("<table class='tsic-grid' role='table'>");
    }

    /// <summary>
    /// Adds a caption to the table with encoded text.
    /// </summary>
    public static void AddCaption(StringBuilder sb, string caption, bool emailMode)
    {
        var safe = WebUtility.HtmlEncode(caption);
        if (emailMode)
        {
            sb.AppendFormat("<div style='font-weight:600;margin:4px 0;'>{0}</div>", safe);
            sb.AppendFormat("<caption style='caption-side:top;text-align:left;font-weight:600;padding:4px 6px;'>{0}</caption>", safe);
        }
        else
        {
            sb.AppendFormat("<caption class='tsic-caption'>{0}</caption>", safe);
        }
    }

    /// <summary>
    /// Starts the table header section.
    /// </summary>
    public static void StartHead(StringBuilder sb) => sb.Append("<thead>");

    /// <summary>
    /// Adds a header row with encoded column headers.
    /// </summary>
    public static void AddHeaderRow(StringBuilder sb, params string[] headers)
    {
        sb.Append("<tr>");
        foreach (var h in headers)
            sb.AppendFormat("<th scope='col' class='tsic-grid-header'>{0}</th>", WebUtility.HtmlEncode(h));
        sb.Append("</tr>");
    }

    /// <summary>
    /// Ends the header section and starts the body section.
    /// </summary>
    public static void EndHeadStartBody(StringBuilder sb) => sb.Append("</thead><tbody>");

    /// <summary>
    /// Adds a data row with cells.
    /// </summary>
    public static void AddRow(StringBuilder sb, params string?[] cells)
    {
        sb.Append("<tr>");
        foreach (var c in cells)
            sb.AppendFormat("<td class='tsic-grid-cell'>{0}</td>", c ?? string.Empty);
        sb.Append("</tr>");
    }

    /// <summary>
    /// Ends the body section and starts the footer section.
    /// </summary>
    public static void EndBodyStartFoot(StringBuilder sb) => sb.Append("</tbody><tfoot>");

    /// <summary>
    /// Adds a footer row with the first cell as a header.
    /// </summary>
    public static void AddFooterRow(StringBuilder sb, params string[] cells)
    {
        if (cells.Length == 0) return;
        sb.Append("<tr>");
        sb.AppendFormat("<th scope='row' class='tsic-grid-footer-header'>{0}</th>", cells[0]);
        for (int i = 1; i < cells.Length; i++)
            sb.AppendFormat("<td class='tsic-grid-footer-cell'>{0}</td>", cells[i]);
        sb.Append("</tr>");
    }

    /// <summary>
    /// Ends the footer and table.
    /// </summary>
    public static void EndFootEndTable(StringBuilder sb) => sb.Append("</tfoot></table>");

    /// <summary>
    /// Ends only the body section (no footer).
    /// </summary>
    public static void EndBodyOnly(StringBuilder sb) => sb.Append("</tbody>");

    /// <summary>
    /// Ends only the table (no footer).
    /// </summary>
    public static void EndTableOnly(StringBuilder sb) => sb.Append("</table>");

    /// <summary>
    /// Formats a decimal value as currency.
    /// </summary>
    public static string FormatCurrency(decimal value) => value.ToString("C");
}

