using System.Drawing;
using Syncfusion.XlsIO;
using TSIC.API.Utilities;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Scheduling;

public class MasterScheduleService : IMasterScheduleService
{
    private readonly IScheduleRepository _scheduleRepo;

    public MasterScheduleService(IScheduleRepository scheduleRepo)
    {
        _scheduleRepo = scheduleRepo;
    }

    public async Task<MasterScheduleResponse> GetMasterScheduleAsync(
        Guid jobId, bool includeReferees, CancellationToken ct = default)
    {
        // 1. Get all games (empty filter = no filtering)
        var games = await _scheduleRepo.GetFilteredGamesAsync(jobId, new ScheduleFilterRequest(), ct);

        if (games.Count == 0)
        {
            return new MasterScheduleResponse
            {
                Days = [],
                FieldColumns = [],
                TotalGames = 0,
            };
        }

        // 2. Extract distinct field columns (sorted alphabetically)
        var fieldColumns = games
            .Where(g => !string.IsNullOrWhiteSpace(g.FName))
            .Select(g => g.FName!)
            .Distinct()
            .OrderBy(f => f)
            .ToList();

        // 3. Optionally load referee assignments
        Dictionary<int, List<string>>? refLookup = null;
        if (includeReferees)
        {
            var gids = games.Select(g => g.Gid).ToList();
            refLookup = await _scheduleRepo.GetRefereeAssignmentsForGamesAsync(gids, ct);
        }

        // 4. Group by date → build days
        var days = games
            .Where(g => g.GDate.HasValue)
            .GroupBy(g => g.GDate!.Value.Date)
            .OrderBy(dayGroup => dayGroup.Key)
            .Select(dayGroup =>
            {
                var date = dayGroup.Key;

                // Group by time within the day
                var rows = dayGroup
                    .GroupBy(g => new TimeOnly(g.GDate!.Value.Hour, g.GDate!.Value.Minute))
                    .OrderBy(timeGroup => timeGroup.Key)
                    .Select(timeGroup =>
                    {
                        var sortKey = date.Add(timeGroup.Key.ToTimeSpan());

                        // Build cells aligned to field columns
                        var cells = fieldColumns.Select(fieldName =>
                        {
                            var game = timeGroup.FirstOrDefault(g =>
                                string.Equals(g.FName, fieldName, StringComparison.OrdinalIgnoreCase));

                            if (game == null) return null;

                            var color = game.Agegroup?.Color;

                            return new MasterScheduleCell
                            {
                                Gid = game.Gid,
                                Round = game.Rnd ?? 0,
                                T1Name = game.T1Name ?? "",
                                T2Name = game.T2Name ?? "",
                                AgDiv = $"{game.AgegroupName}:{game.DivName}",
                                Color = color,
                                ContrastColor = ColorUtility.GetContrastColor(color),
                                T1Score = game.T1Score,
                                T2Score = game.T2Score,
                                T1Ann = game.T1Ann,
                                T2Ann = game.T2Ann,
                                GStatusCode = game.GStatusCode,
                                Referees = refLookup?.GetValueOrDefault(game.Gid),
                            };
                        }).ToList();

                        return new MasterScheduleRow
                        {
                            TimeLabel = sortKey.ToString("h:mm tt"),
                            SortKey = sortKey,
                            Cells = cells!,
                        };
                    })
                    .ToList();

                return new MasterScheduleDay
                {
                    DayLabel = date.ToString("dddd, MMMM d, yyyy"),
                    ShortLabel = date.ToString("ddd MMM d"),
                    GameCount = dayGroup.Count(),
                    Rows = rows,
                };
            })
            .ToList();

        return new MasterScheduleResponse
        {
            Days = days,
            FieldColumns = fieldColumns,
            TotalGames = games.Count,
        };
    }

    public async Task<byte[]> ExportExcelAsync(
        Guid jobId, bool includeReferees, int? dayIndex, CancellationToken ct = default)
    {
        var data = await GetMasterScheduleAsync(jobId, includeReferees, ct);

        using var excelEngine = new ExcelEngine();
        IApplication application = excelEngine.Excel;
        application.DefaultVersion = ExcelVersion.Xlsx;
        IWorkbook workbook = application.Workbooks.Create(1);

        var daysToExport = dayIndex.HasValue && dayIndex.Value < data.Days.Count
            ? [data.Days[dayIndex.Value]]
            : data.Days;

        var sheetsCreated = 0;
        foreach (var day in daysToExport)
        {
            // Excel sheet name limit: 31 chars
            var sheetName = day.ShortLabel.Length > 31
                ? day.ShortLabel[..31]
                : day.ShortLabel;

            // Reuse the default sheet for the first day, then create new ones — avoids a
            // leftover "Sheet1" (XlsIO workbooks are created with one sheet already present).
            var ws = sheetsCreated == 0 ? workbook.Worksheets[0] : workbook.Worksheets.Create(sheetName);
            ws.Name = sheetName;
            sheetsCreated++;

            // ── Header row ──
            ws.Range[1, 1].Text = "Time";
            for (var c = 0; c < data.FieldColumns.Count; c++)
            {
                ws.Range[1, c + 2].Text = data.FieldColumns[c];
            }

            // Style header
            var headerRange = ws.Range[1, 1, 1, data.FieldColumns.Count + 1];
            headerRange.CellStyle.Font.Bold = true;
            headerRange.CellStyle.Font.Size = 10;
            headerRange.CellStyle.Color = Syncfusion.Drawing.Color.FromArgb(230, 230, 230);
            headerRange.CellStyle.Borders[ExcelBordersIndex.EdgeBottom].LineStyle = ExcelLineStyle.Thin;

            // ── Data rows ──
            var row = 2;
            foreach (var schedRow in day.Rows)
            {
                ws.Range[row, 1].Text = schedRow.TimeLabel;
                ws.Range[row, 1].CellStyle.Font.Bold = true;
                ws.Range[row, 1].CellStyle.VerticalAlignment = ExcelVAlign.VAlignTop;

                for (var c = 0; c < schedRow.Cells.Count; c++)
                {
                    var cell = schedRow.Cells[c];
                    if (cell == null) continue;

                    var excelCell = ws.Range[row, c + 2];

                    // Multi-line cell content
                    var lines = new List<string> { cell.AgDiv, cell.T1Name, "  vs", cell.T2Name };
                    if (cell.T1Score != null)
                        lines.Add($"({cell.T1Score}–{cell.T2Score})");
                    if (cell.Referees is { Count: > 0 })
                        lines.Add($"[{string.Join(", ", cell.Referees)}]");

                    excelCell.Text = string.Join("\n", lines);
                    excelCell.CellStyle.WrapText = true;
                    excelCell.CellStyle.VerticalAlignment = ExcelVAlign.VAlignTop;
                    excelCell.CellStyle.Font.Size = 9;
                    excelCell.BorderAround(ExcelLineStyle.Thin);

                    // Agegroup color background
                    if (!string.IsNullOrEmpty(cell.Color))
                    {
                        try
                        {
                            excelCell.CellStyle.Color = ColorTranslator.FromHtml(cell.Color).ToXlsioColor();
                            excelCell.CellStyle.Font.RGBColor =
                                ColorTranslator.FromHtml(cell.ContrastColor ?? "#000").ToXlsioColor();
                        }
                        catch
                        {
                            // Invalid color string — skip formatting
                        }
                    }
                }

                row++;
            }

            // Auto-fit columns (clamp to 12..25 like the legacy export)
            ws.AutofitColumnsClamped(12, 25);

            // Print setup
            ws.PageSetup.Orientation = ExcelPageOrientation.Landscape;
            ws.PageSetup.IsFitToPage = true;
            ws.PageSetup.FitToPagesWide = 1;
            ws.PageSetup.FitToPagesTall = 0;
            ws.PageSetup.PrintTitleRows = "$1:$1";
        }

        return workbook.ToByteArray();
    }
}
