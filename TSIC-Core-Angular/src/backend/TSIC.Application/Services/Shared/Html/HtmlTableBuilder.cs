using System.Net;
using System.Text;

namespace TSIC.Application.Services.Shared.Html;

/// <summary>
/// Dual-mode HTML emitters shared by every token builder. Web UI output uses the tsic-* CSS
/// classes (styles/_tables.scss); email output carries full inline styles because email clients
/// ignore stylesheets. Tables are written through <see cref="HtmlTable"/>, which owns section
/// bookkeeping (thead/tbody/tfoot), zebra striping, and the single caption band — the caption is
/// emitted BEFORE the &lt;table&gt; tag in email mode because a &lt;div&gt; inside table markup
/// gets hoisted unpredictably by email clients (the old dual div+caption emit rendered every
/// table title twice, once as a stray row).
/// </summary>
public static class HtmlTableBuilder
{
    /// <summary>
    /// A table cell carrying render hints. Plain strings passed to the row methods keep the
    /// historical contract (pre-encoded / raw HTML, left-aligned); wrap money and counts in a
    /// numeric cell via <see cref="FormatCurrency"/> or <see cref="Num"/> to right-align them.
    /// </summary>
    public readonly record struct Cell(string Html, bool Numeric)
    {
        public override string ToString() => Html;
    }

    /// <summary>Formats a decimal as currency in a right-aligned numeric cell.</summary>
    public static Cell FormatCurrency(decimal value) => new(value.ToString("C"), true);

    /// <summary>Encodes plain text into a right-aligned numeric cell (headers, counts).</summary>
    public static Cell Num(string text) => new(WebUtility.HtmlEncode(text), true);

    /// <summary>
    /// Wraps content in an amber warning callout (e.g. inactive player notice). Email styling
    /// matches the EcheckPendingBanner house style. Dual-mode: CSS class for web, inline for email.
    /// </summary>
    public static string RenderWarningBlock(string innerHtml, bool emailMode)
    {
        if (emailMode)
            return "<div style=\"background:#fff7e6;border:1px solid #ffd591;border-left:4px solid #fa8c16;" +
                   "padding:14px 16px;margin:0 0 18px;border-radius:4px;font-family:Arial,Helvetica,sans-serif;" +
                   "color:#612500;font-size:13px;line-height:1.5;\">" + innerHtml + "</div>";
        return $"<div class='tsic-warning-block'>{innerHtml}</div>";
    }

    /// <summary>
    /// Wraps labelled waiver content. Label is encoded; body is pre-rendered HTML.
    /// </summary>
    public static string RenderWaiverBlock(string label, string bodyHtml, bool emailMode)
    {
        var safeLabel = WebUtility.HtmlEncode(label);
        if (emailMode)
            return "<div style='margin:8px 0;font-family:Arial,Helvetica,sans-serif;font-size:13px;color:#1f2937;line-height:1.5;'>" +
                   $"<strong style='display:block;margin-bottom:4px;'>{safeLabel}</strong>{bodyHtml}</div>";
        return $"<div class='tsic-waiver-block'><strong class='tsic-waiver-label'>{safeLabel}</strong>{bodyHtml}</div>";
    }

    /// <summary>
    /// Wraps a &lt;ul&gt; choices list with consistent class.
    /// </summary>
    public static string RenderChoicesList(string innerLiHtml, bool emailMode)
    {
        if (emailMode)
            return $"<ul style='margin:4px 0;padding-left:20px;font-family:Arial,Helvetica,sans-serif;font-size:13px;color:#1f2937;'>{innerLiHtml}</ul>";
        return $"<ul class='tsic-choices-list'>{innerLiHtml}</ul>";
    }
}

/// <summary>
/// Writes one table into a caller-owned StringBuilder. Sections are managed implicitly:
/// <see cref="HeaderRow"/> emits a complete thead, the first <see cref="Row"/> opens tbody,
/// <see cref="FooterRow"/> closes tbody and emits a complete tfoot, and <see cref="End"/>
/// closes whatever is open. Email mode inline-styles every element (tinted header row, zebra
/// body rows, footer band); web mode emits the tsic-* classes unchanged.
/// </summary>
public sealed class HtmlTable
{
    private const string FontStack = "font-family:Arial,Helvetica,sans-serif;";
    private const string CaptionStyle = FontStack + "font-size:14px;font-weight:700;color:#1f2937;padding:12px 2px 6px;";
    private const string TableStyle = "border-collapse:collapse;width:100%;" + FontStack + "font-size:13px;color:#1f2937;margin:0 0 16px;";
    private const string ThStyle = "background:#f1f5f9;color:#334155;text-align:left;padding:6px 8px;border:1px solid #cbd5e1;font-size:12px;";
    private const string TdStyle = "padding:6px 8px;border:1px solid #e2e8f0;";
    private const string TdZebra = "background:#f8fafc;";
    private const string FootStyle = "background:#f1f5f9;font-weight:600;text-align:left;padding:6px 8px;border:1px solid #cbd5e1;";
    private const string NumStyle = "text-align:right;white-space:nowrap;";

    private readonly StringBuilder _sb;
    private readonly bool _email;
    private bool _bodyOpen;
    private int _bodyRowIndex;

    public HtmlTable(StringBuilder sb, bool emailMode, string? caption = null)
    {
        _sb = sb;
        _email = emailMode;
        if (emailMode)
        {
            if (!string.IsNullOrWhiteSpace(caption))
                _sb.AppendFormat("<div style='{0}'>{1}</div>", CaptionStyle, WebUtility.HtmlEncode(caption));
            _sb.Append("<table role='table' cellpadding='0' cellspacing='0' style='").Append(TableStyle).Append("'>");
        }
        else
        {
            _sb.Append("<table class='tsic-grid' role='table'>");
            if (!string.IsNullOrWhiteSpace(caption))
                _sb.AppendFormat("<caption class='tsic-caption'>{0}</caption>", WebUtility.HtmlEncode(caption));
        }
    }

    /// <summary>Emits the complete thead. Strings are encoded; use HtmlTableBuilder.Num for numeric columns.</summary>
    public void HeaderRow(params object?[] headers)
    {
        _sb.Append("<thead><tr>");
        foreach (var h in headers)
        {
            var (html, numeric) = Normalize(h, encodePlainStrings: true);
            if (_email)
                _sb.AppendFormat("<th scope='col' style='{0}{1}'>{2}</th>", ThStyle, numeric ? NumStyle : string.Empty, html);
            else
                _sb.AppendFormat("<th scope='col' class='tsic-grid-header{0}'>{1}</th>", numeric ? " tsic-cell-num" : string.Empty, html);
        }
        _sb.Append("</tr></thead>");
    }

    /// <summary>Emits a body row. Strings are raw/pre-encoded HTML (historical contract).</summary>
    public void Row(params object?[] cells)
    {
        if (!_bodyOpen)
        {
            _sb.Append("<tbody>");
            _bodyOpen = true;
        }
        var zebra = _email && (_bodyRowIndex % 2 == 1);
        _bodyRowIndex++;
        _sb.Append("<tr>");
        foreach (var c in cells)
        {
            var (html, numeric) = Normalize(c, encodePlainStrings: false);
            if (_email)
                _sb.AppendFormat("<td style='{0}{1}{2}'>{3}</td>", TdStyle, zebra ? TdZebra : string.Empty, numeric ? NumStyle : string.Empty, html);
            else
                _sb.AppendFormat("<td class='tsic-grid-cell{0}'>{1}</td>", numeric ? " tsic-cell-num" : string.Empty, html);
        }
        _sb.Append("</tr>");
    }

    /// <summary>Closes the body and emits the complete tfoot; first cell is the row header.</summary>
    public void FooterRow(params object?[] cells)
    {
        if (cells.Length == 0) return;
        CloseBody();
        _sb.Append("<tfoot><tr>");
        var (first, _) = Normalize(cells[0], encodePlainStrings: false);
        if (_email)
            _sb.AppendFormat("<th scope='row' style='{0}'>{1}</th>", FootStyle, first);
        else
            _sb.AppendFormat("<th scope='row' class='tsic-grid-footer-header'>{0}</th>", first);
        for (int i = 1; i < cells.Length; i++)
        {
            var (html, numeric) = Normalize(cells[i], encodePlainStrings: false);
            if (_email)
                _sb.AppendFormat("<td style='{0}{1}'>{2}</td>", FootStyle, numeric ? NumStyle : string.Empty, html);
            else
                _sb.AppendFormat("<td class='tsic-grid-footer-cell{0}'>{1}</td>", numeric ? " tsic-cell-num" : string.Empty, html);
        }
        _sb.Append("</tr></tfoot>");
    }

    /// <summary>Closes any open section and the table.</summary>
    public void End()
    {
        CloseBody();
        _sb.Append("</table>");
    }

    private void CloseBody()
    {
        if (_bodyOpen)
        {
            _sb.Append("</tbody>");
            _bodyOpen = false;
        }
    }

    private static (string Html, bool Numeric) Normalize(object? cell, bool encodePlainStrings) => cell switch
    {
        null => (string.Empty, false),
        HtmlTableBuilder.Cell c => (c.Html, c.Numeric),
        string s => (encodePlainStrings ? WebUtility.HtmlEncode(s) : s, false),
        _ => (WebUtility.HtmlEncode(cell.ToString() ?? string.Empty), false),
    };
}
