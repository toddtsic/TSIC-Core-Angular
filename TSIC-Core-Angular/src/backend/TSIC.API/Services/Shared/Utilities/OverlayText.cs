using System.Net;
using System.Text.RegularExpressions;

namespace TSIC.API.Services.Shared.Utilities;

/// <summary>
/// Storage/display conversion for job banner overlay text
/// (JobDisplayOptions.parallaxSlide1Text1/Text2).
///
/// Stored values are inconsistent by era: legacy rows hold HTML-encoded rich text with inline
/// &lt;span&gt;/&lt;i&gt; markup, while anything written since holds plain text with &lt;br&gt;
/// line joins. Everything that reads the column for editing normalizes through
/// <see cref="ToPlainText"/>; everything that writes it goes back through
/// <see cref="ToStoredHtml"/>.
///
/// Shared by the Branding tab (JobConfigService) and job clone, which must produce byte-identical
/// storage — the clone workbench previews the wording it is about to write, and a preview that
/// normalizes differently from the writer is a preview that lies.
/// </summary>
public static class OverlayText
{
    /// <summary>
    /// Stored form → plain text with '\n' line breaks. Decodes HTML entities, converts
    /// &lt;br&gt; variants to newlines, strips surviving tags, and drops blank lines
    /// (including the bare \r that legacy `&lt;br /&gt;\r\n` patterns leave behind).
    /// Returns null when nothing survives.
    /// </summary>
    public static string? ToPlainText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var text = WebUtility.HtmlDecode(raw);
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", "");
        text = text.Replace("\u00A0", " ");

        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        var result = string.Join("\n", lines);
        return result.Length > 0 ? result : null;
    }

    /// <summary>
    /// Plain text (textarea input) → stored form. Returns null for empty/whitespace-only input,
    /// so "cleared in the UI" lands as NULL rather than an empty string the banner would
    /// still treat as present.
    /// </summary>
    public static string? ToStoredHtml(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return text.Trim().Replace("\r\n", "<br>").Replace("\n", "<br>");
    }
}
