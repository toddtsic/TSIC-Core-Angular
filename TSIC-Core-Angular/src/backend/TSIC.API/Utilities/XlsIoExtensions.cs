using System.Globalization;
using Syncfusion.XlsIO;

namespace TSIC.API.Utilities;

/// <summary>
/// Small helpers that smooth the Syncfusion XlsIO surface used across the
/// Excel-producing services (reports, schedules, upload templates). Centralizes
/// the EPPlus → XlsIO idioms that don't map one-to-one: typed cell assignment,
/// "border around" a range, autofit with min/max clamp, and save-to-bytes.
/// </summary>
internal static class XlsIoExtensions
{
    /// <summary>
    /// Excel/OLE-Automation date epoch. DateTime values before this can't be
    /// represented as an Excel date cell; XlsIO's OADate conversion either throws
    /// (year &lt; 100 → TicksToOADate OverflowException, e.g. a 0001-01-01 sentinel
    /// from a nullable SQL date) or renders a broken cell (year 100–1899).
    /// </summary>
    private static readonly DateTime ExcelEpoch = new(1900, 1, 1);

    /// <summary>
    /// Assigns a CLR value to a cell using the correct XlsIO typed setter.
    /// XlsIO has no single object setter equivalent to EPPlus's
    /// <c>Cells[r,c].Value = object</c>, so dispatch on the runtime type.
    /// null / DBNull leave the cell empty.
    /// </summary>
    public static void SetCellValue(this IRange cell, object? value)
    {
        switch (value)
        {
            case null:
            case DBNull:
                return;
            case DateTime dt:
                // Pre-1900 values are real rows in the data — overwhelmingly the
                // 0001-01-01 "no date" sentinel, plus stray garbage years. They
                // overflow XlsIO's OADate conversion (TicksToOADate throws
                // "Not a legal OleAut date") or render a broken Excel cell. Treat
                // them as "no date": leave the cell blank, same as null/DBNull.
                if (dt >= ExcelEpoch)
                    cell.DateTime = dt;
                break;
            case bool b:
                cell.Boolean = b;
                break;
            case string s:
                cell.Text = s;
                break;
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                cell.Number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                break;
            default:
                cell.Text = value.ToString() ?? string.Empty;
                break;
        }
    }

    /// <summary>EPPlus <c>Border.BorderAround</c> equivalent — sets all four edges.</summary>
    public static void BorderAround(this IRange range, ExcelLineStyle style = ExcelLineStyle.Thin)
    {
        range.CellStyle.Borders[ExcelBordersIndex.EdgeTop].LineStyle = style;
        range.CellStyle.Borders[ExcelBordersIndex.EdgeBottom].LineStyle = style;
        range.CellStyle.Borders[ExcelBordersIndex.EdgeLeft].LineStyle = style;
        range.CellStyle.Borders[ExcelBordersIndex.EdgeRight].LineStyle = style;
    }

    /// <summary>
    /// Autofit all used columns, then clamp each width to [min, max].
    /// Mirrors EPPlus's <c>AutoFitColumns(minWidth, maxWidth)</c> (XlsIO autofit
    /// has no min/max parameters). Widths are in Excel character units.
    /// </summary>
    public static void AutofitColumnsClamped(this IWorksheet ws, double min, double max)
    {
        if (ws.UsedRange == null) return;
        ws.UsedRange.AutofitColumns();
        var lastCol = ws.UsedRange.LastColumn;
        for (var c = 1; c <= lastCol; c++)
        {
            var w = ws.GetColumnWidth(c);
            if (w < min) ws.SetColumnWidth(c, min);
            else if (w > max) ws.SetColumnWidth(c, max);
        }
    }

    /// <summary>
    /// Converts a <see cref="System.Drawing.Color"/> to XlsIO's <c>Syncfusion.Drawing.Color</c>.
    /// XlsIO on .NET Core uses its own color type, so System.Drawing colors (e.g. from
    /// <c>ColorTranslator.FromHtml</c>) can't be assigned to styles directly.
    /// </summary>
    public static Syncfusion.Drawing.Color ToXlsioColor(this System.Drawing.Color c)
        => Syncfusion.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);

    /// <summary>EPPlus <c>GetAsByteArray()</c> equivalent.</summary>
    public static byte[] ToByteArray(this IWorkbook workbook)
    {
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }
}
