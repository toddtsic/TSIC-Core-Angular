using Syncfusion.XlsIO;
using TSIC.API.Utilities;

namespace TSIC.API.Services.Shared.Files;

/// <summary>
/// One sheet: a name, a header row, and the rows under it.
/// </summary>
public sealed class ExcelSheet
{
    public required string Name { get; init; }
    public List<string> Columns { get; } = new();
    public List<object?[]> Rows { get; } = new();

    /// <summary>Convenience for the common "build the header once, then append" shape.</summary>
    public ExcelSheet WithColumns(params string[] columns)
    {
        Columns.AddRange(columns);
        return this;
    }
}

/// <summary>
/// Renders sheet models to an .xlsx. The ONE place a workbook is built.
/// </summary>
/// <remarks>
/// Extracted from <c>ReportingService</c>, which is still its largest caller — a
/// behaviour-preserving port of the legacy <c>BuildExcelFromDataReader</c> Excel path (reuse the
/// default sheet, header row, DateTime formatting). Store exports use it too; a second
/// hand-rolled writer would drift on exactly the details that are easy to get wrong, and
/// <see cref="SanitizeSheetName"/> below is the record of two of them.
/// </remarks>
public static class ExcelWorkbookWriter
{
    public static byte[] Build(List<ExcelSheet> sheets)
    {
        using var excelEngine = new ExcelEngine();
        IApplication application = excelEngine.Excel;
        application.DefaultVersion = ExcelVersion.Xlsx;
        IWorkbook workbook = application.Workbooks.Create(1);
        var sheetsCreated = 0;
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Reuse the default sheet XlsIO creates for the first sheet, then create new
        // ones — preserves EPPlus's "start empty, add named sheets" behavior without
        // leaving a stray "Sheet1" in the output. Names are sanitized here — the one
        // place they enter the workbook — because they come from legacy SPs' "QA Test:"
        // markers, which can violate Excel's sheet-name rules (e.g. RegsaverRegistrants_ALL
        // emits "STEPS X-Check By Year/Month"; the '/' made XlsIO throw "Sheet Name is
        // InValid". EPPlus, which the legacy app used, never validated).
        IWorksheet AddWorksheet(string name)
        {
            var safeName = SanitizeSheetName(name, usedNames);
            var sheet = sheetsCreated == 0 ? workbook.Worksheets[0] : workbook.Worksheets.Create(safeName);
            sheet.Name = safeName;
            sheetsCreated++;
            return sheet;
        }

        foreach (var sheetData in sheets)
        {
            var worksheet = AddWorksheet(sheetData.Name);

            for (var col = 0; col < sheetData.Columns.Count; col++)
            {
                worksheet.Range[1, col + 1].SetCellValue(sheetData.Columns[col]);
            }

            for (var r = 0; r < sheetData.Rows.Count; r++)
            {
                var row = sheetData.Rows[r];
                for (var col = 0; col < row.Length; col++)
                {
                    var cellValue = row[col];
                    var target = worksheet.Range[r + 2, col + 1];

                    if (cellValue is DateTime)
                    {
                        target.SetCellValue(cellValue);
                        target.NumberFormat = "mm/dd/yyyy";
                    }
                    else
                    {
                        target.SetCellValue(cellValue);
                    }
                }
            }
        }

        return workbook.ToByteArray();
    }

    /// <summary>
    /// Makes a sheet name legal for Excel: replaces <c>: \ / ? * [ ]</c> with <c>-</c>,
    /// strips wrapping apostrophes, caps at 31 chars, falls back to "Sheet" when empty,
    /// and uniquifies with an " (n)" suffix (duplicate names also throw in XlsIO).
    /// </summary>
    public static string SanitizeSheetName(string name, HashSet<string> usedNames)
    {
        var cleaned = new string(name
            .Select(c => c is ':' or '\\' or '/' or '?' or '*' or '[' or ']' ? '-' : c)
            .ToArray()).Trim().Trim('\'');
        if (cleaned.Length == 0)
        {
            cleaned = "Sheet";
        }
        if (cleaned.Length > 31)
        {
            cleaned = cleaned[..31].TrimEnd();
        }

        var candidate = cleaned;
        var n = 2;
        while (!usedNames.Add(candidate))
        {
            var suffix = $" ({n++})";
            candidate = cleaned.Length + suffix.Length <= 31
                ? cleaned + suffix
                : cleaned[..(31 - suffix.Length)].TrimEnd() + suffix;
        }
        return candidate;
    }

    /// <summary>The MIME type every .xlsx response must carry.</summary>
    public const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}
