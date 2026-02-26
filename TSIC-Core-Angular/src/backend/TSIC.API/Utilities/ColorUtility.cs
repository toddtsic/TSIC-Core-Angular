namespace TSIC.API.Utilities;

/// <summary>
/// Server-side contrast color calculation using ITU-R BT.601 luminance.
/// Same formula as frontend games-tab.component.ts.
/// </summary>
public static class ColorUtility
{
    /// <summary>
    /// Returns "#000" or "#fff" for readable text on the given background color.
    /// </summary>
    public static string GetContrastColor(string? hexColor)
    {
        if (string.IsNullOrEmpty(hexColor)) return "#000";

        var hex = hexColor.TrimStart('#');
        if (hex.Length < 6) return "#000";

        var r = Convert.ToInt32(hex[..2], 16);
        var g = Convert.ToInt32(hex[2..4], 16);
        var b = Convert.ToInt32(hex[4..6], 16);

        var luminance = 0.299 * r + 0.587 * g + 0.114 * b;
        return luminance > 150 ? "#000" : "#fff";
    }
}
