using OfficeOpenXml;
using OfficeOpenXml.Style;
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

        ExcelPackage.License.SetNonCommercialPersonal("Todd Greenwald");
        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("Uniform Numbers");

        // Header row
        var headers = new[] { "RegistrationId", "FirstName", "LastName", "TeamName", "UniformNo", "DayGroup" };
        for (var col = 1; col <= headers.Length; col++)
        {
            ws.Cells[1, col].Value = headers[col - 1];
        }

        // Style header
        using (var headerRange = ws.Cells[1, 1, 1, headers.Length])
        {
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
            headerRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(68, 114, 196));
            headerRange.Style.Font.Color.SetColor(System.Drawing.Color.White);
        }

        // Data rows
        for (var i = 0; i < roster.Count; i++)
        {
            var row = i + 2;
            var player = roster[i];
            ws.Cells[row, 1].Value = player.RegistrationId.ToString();
            ws.Cells[row, 2].Value = player.FirstName;
            ws.Cells[row, 3].Value = player.LastName;
            ws.Cells[row, 4].Value = player.TeamName;
            ws.Cells[row, 5].Value = player.UniformNo ?? string.Empty;
            ws.Cells[row, 6].Value = player.DayGroup ?? string.Empty;
        }

        // Read-only columns (A-D) get gray background to signal "don't edit"
        if (roster.Count > 0)
        {
            using var readOnlyRange = ws.Cells[2, 1, roster.Count + 1, 4];
            readOnlyRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
            readOnlyRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(242, 242, 242));
            readOnlyRange.Style.Font.Color.SetColor(System.Drawing.Color.FromArgb(128, 128, 128));
        }

        // Auto-fit columns
        ws.Cells[ws.Dimension.Address].AutoFitColumns();

        return await package.GetAsByteArrayAsync();
    }

    public async Task<UniformUploadResultDto> ProcessUploadAsync(Guid jobId, Stream fileStream, CancellationToken ct = default)
    {
        ExcelPackage.License.SetNonCommercialPersonal("Todd Greenwald");
        using var package = new ExcelPackage(fileStream);

        var ws = package.Workbook.Worksheets.FirstOrDefault();
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
        var rowCount = ws.Dimension?.Rows ?? 0;
        for (var row = 2; row <= rowCount; row++)
        {
            var regIdRaw = ws.Cells[row, colMap.RegIdCol].Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(regIdRaw))
                continue; // skip blank rows

            if (!Guid.TryParse(regIdRaw, out var regId))
            {
                errors.Add(new UniformUploadRowError { Row = row, RegistrationId = regIdRaw, Reason = "Invalid RegistrationId format." });
                continue;
            }

            var uniformNo = colMap.UniformNoCol > 0 ? ws.Cells[row, colMap.UniformNoCol].Text?.Trim() : null;
            var dayGroup = colMap.DayGroupCol > 0 ? ws.Cells[row, colMap.DayGroupCol].Text?.Trim() : null;

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

    private static (int RegIdCol, int UniformNoCol, int DayGroupCol) ResolveColumnMap(ExcelWorksheet ws)
    {
        int regIdCol = 0, uniformNoCol = 0, dayGroupCol = 0;
        var colCount = ws.Dimension?.Columns ?? 0;

        for (var col = 1; col <= colCount; col++)
        {
            var header = ws.Cells[1, col].Text?.Trim() ?? "";
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
