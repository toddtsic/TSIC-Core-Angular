using Syncfusion.XlsIO;
using TSIC.API.Utilities;
using TSIC.Contracts.Dtos.UniformUpload;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Admin;

public class UniformUploadService : IUniformUploadService
{
    private readonly IRegistrationRepository _registrationRepo;

    public UniformUploadService(IRegistrationRepository registrationRepo)
    {
        _registrationRepo = registrationRepo;
    }

    public async Task<byte[]> GenerateTemplateAsync(Guid jobId, CancellationToken ct = default)
    {
        var roster = await _registrationRepo.GetPlayerRosterForTemplateAsync(jobId, ct);

        using var excelEngine = new ExcelEngine();
        IApplication application = excelEngine.Excel;
        application.DefaultVersion = ExcelVersion.Xlsx;
        IWorkbook workbook = application.Workbooks.Create(1);
        var ws = workbook.Worksheets[0];
        ws.Name = "Uniform Numbers";

        // Header row
        var headers = new[] { "RegistrationId", "FirstName", "LastName", "TeamName", "UniformNo", "DayGroup" };
        for (var col = 1; col <= headers.Length; col++)
        {
            ws.Range[1, col].Text = headers[col - 1];
        }

        // Style header
        var headerRange = ws.Range[1, 1, 1, headers.Length];
        headerRange.CellStyle.Font.Bold = true;
        headerRange.CellStyle.Color = Syncfusion.Drawing.Color.FromArgb(68, 114, 196);
        headerRange.CellStyle.Font.RGBColor = Syncfusion.Drawing.Color.White;

        // Data rows
        for (var i = 0; i < roster.Count; i++)
        {
            var row = i + 2;
            var player = roster[i];
            ws.Range[row, 1].SetCellValue(player.RegistrationId.ToString());
            ws.Range[row, 2].SetCellValue(player.FirstName);
            ws.Range[row, 3].SetCellValue(player.LastName);
            ws.Range[row, 4].SetCellValue(player.TeamName);
            ws.Range[row, 5].SetCellValue(player.UniformNo ?? string.Empty);
            ws.Range[row, 6].SetCellValue(player.DayGroup ?? string.Empty);
        }

        // Read-only columns (A-D) get gray background to signal "don't edit"
        if (roster.Count > 0)
        {
            var readOnlyRange = ws.Range[2, 1, roster.Count + 1, 4];
            readOnlyRange.CellStyle.Color = Syncfusion.Drawing.Color.FromArgb(242, 242, 242);
            readOnlyRange.CellStyle.Font.RGBColor = Syncfusion.Drawing.Color.FromArgb(128, 128, 128);
        }

        // Auto-fit columns
        ws.UsedRange.AutofitColumns();

        return workbook.ToByteArray();
    }

    public async Task<UniformUploadResultDto> ProcessUploadAsync(Guid jobId, Stream fileStream, CancellationToken ct = default)
    {
        using var excelEngine = new ExcelEngine();
        IApplication application = excelEngine.Excel;
        IWorkbook workbook = application.Workbooks.Open(fileStream);

        var ws = workbook.Worksheets.Count > 0 ? workbook.Worksheets[0] : null;
        if (ws == null)
        {
            return new UniformUploadResultDto
            {
                TotalRows = 0,
                UpdatedCount = 0,
                SkippedCount = 0,
                ErrorCount = 1,
                Errors = [new UniformUploadRowError { Row = 0, RegistrationId = "", Reason = "No worksheet found in the uploaded file." }]
            };
        }

        var errors = new List<UniformUploadRowError>();
        var parsedRows = new List<(int Row, Guid RegId, string? UniformNo, string? DayGroup)>();

        // Find column indices from header row
        var colMap = ResolveColumnMap(ws);
        if (colMap.RegIdCol == 0)
        {
            return new UniformUploadResultDto
            {
                TotalRows = 0,
                UpdatedCount = 0,
                SkippedCount = 0,
                ErrorCount = 1,
                Errors = [new UniformUploadRowError { Row = 1, RegistrationId = "", Reason = "Missing required column 'RegistrationId' in header row." }]
            };
        }

        // Parse data rows (row 2+)
        var rowCount = ws.UsedRange.LastRow;
        for (var row = 2; row <= rowCount; row++)
        {
            var regIdRaw = ws.Range[row, colMap.RegIdCol].DisplayText?.Trim() ?? "";
            if (string.IsNullOrEmpty(regIdRaw))
                continue; // skip blank rows

            if (!Guid.TryParse(regIdRaw, out var regId))
            {
                errors.Add(new UniformUploadRowError { Row = row, RegistrationId = regIdRaw, Reason = "Invalid RegistrationId format." });
                continue;
            }

            var uniformNo = colMap.UniformNoCol > 0 ? ws.Range[row, colMap.UniformNoCol].DisplayText?.Trim() : null;
            var dayGroup = colMap.DayGroupCol > 0 ? ws.Range[row, colMap.DayGroupCol].DisplayText?.Trim() : null;

            parsedRows.Add((row, regId, uniformNo, dayGroup));
        }

        if (parsedRows.Count == 0)
        {
            return new UniformUploadResultDto
            {
                TotalRows = 0,
                UpdatedCount = 0,
                SkippedCount = 0,
                ErrorCount = errors.Count,
                Errors = errors
            };
        }

        // Batch-load all registrations (tracked for updates)
        var regIds = parsedRows.Select(r => r.RegId).Distinct().ToList();
        var registrations = await _registrationRepo.GetByIdsAsync(regIds, ct);
        var regLookup = registrations.ToDictionary(r => r.RegistrationId);

        var updatedCount = 0;
        var skippedCount = 0;

        foreach (var (row, regId, uniformNo, dayGroup) in parsedRows)
        {
            if (!regLookup.TryGetValue(regId, out var reg))
            {
                errors.Add(new UniformUploadRowError { Row = row, RegistrationId = regId.ToString(), Reason = "Registration not found." });
                continue;
            }

            if (reg.JobId != jobId)
            {
                errors.Add(new UniformUploadRowError { Row = row, RegistrationId = regId.ToString(), Reason = "Registration belongs to a different job." });
                continue;
            }

            // Check if anything actually changed
            var uniformChanged = !string.Equals(reg.UniformNo ?? "", uniformNo ?? "", StringComparison.Ordinal);
            var dayGroupChanged = !string.Equals(reg.DayGroup ?? "", dayGroup ?? "", StringComparison.Ordinal);

            if (!uniformChanged && !dayGroupChanged)
            {
                skippedCount++;
                continue;
            }

            reg.UniformNo = string.IsNullOrEmpty(uniformNo) ? null : uniformNo;
            reg.DayGroup = string.IsNullOrEmpty(dayGroup) ? null : dayGroup;
            // reg is already tracked (loaded via GetByIdsAsync without AsNoTracking);
            // do NOT call Update() — it marks ALL columns as modified including the
            // RegistrationAI identity column, which SQL Server rejects.
            updatedCount++;
        }

        if (updatedCount > 0)
        {
            await _registrationRepo.SaveChangesAsync(ct);
        }

        return new UniformUploadResultDto
        {
            TotalRows = parsedRows.Count,
            UpdatedCount = updatedCount,
            SkippedCount = skippedCount,
            ErrorCount = errors.Count,
            Errors = errors
        };
    }

    private static (int RegIdCol, int UniformNoCol, int DayGroupCol) ResolveColumnMap(IWorksheet ws)
    {
        int regIdCol = 0, uniformNoCol = 0, dayGroupCol = 0;
        var colCount = ws.UsedRange.LastColumn;

        for (var col = 1; col <= colCount; col++)
        {
            var header = ws.Range[1, col].DisplayText?.Trim() ?? "";
            if (header.Equals("RegistrationId", StringComparison.OrdinalIgnoreCase))
                regIdCol = col;
            else if (header.Equals("UniformNo", StringComparison.OrdinalIgnoreCase)
                     || header.Equals("AssignedUniformNumber", StringComparison.OrdinalIgnoreCase))
                uniformNoCol = col;
            else if (header.Equals("DayGroup", StringComparison.OrdinalIgnoreCase)
                     || header.Equals("AssignedDayGroup", StringComparison.OrdinalIgnoreCase))
                dayGroupCol = col;
        }

        return (regIdCol, uniformNoCol, dayGroupCol);
    }
}
