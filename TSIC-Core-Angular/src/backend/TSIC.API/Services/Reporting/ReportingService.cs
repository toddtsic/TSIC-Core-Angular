using System.Data.Common;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OfficeOpenXml;
using TSIC.API.Configuration;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Reporting;

public sealed class ReportingService : IReportingService
{
    private readonly IReportingRepository _reportingRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ReportingSettings _settings;

    public ReportingService(
        IReportingRepository reportingRepository,
        IHttpClientFactory httpClientFactory,
        IOptions<ReportingSettings> settings)
    {
        _reportingRepository = reportingRepository;
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
    }

    public async Task<ReportExportResult> ExportCrystalReportAsync(
        string reportName,
        int exportFormat,
        Guid jobId,
        Guid? regId,
        string userId,
        string? strGids = null,
        CancellationToken cancellationToken = default)
    {
        if (exportFormat == 0)
        {
            exportFormat = (int)ReportExportFormat.Pdf;
        }

        var request = new CrystalReportRequest
        {
            RptName = reportName,
            JobId = jobId,
            UserId = userId,
            RegId = regId,
            ExportFormat = exportFormat,
            StrGids = strGids
        };

        var client = _httpClientFactory.CreateClient("CrystalReports");
        var crUrl = $"{_settings.CrystalReportsBaseUrl}CrystalReports/Get";

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync(crUrl, jsonContent, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var errorBytes = Encoding.UTF8.GetBytes($"Crystal Reports export failed: {errorContent}");
            return new ReportExportResult
            {
                FileBytes = errorBytes,
                ContentType = "text/plain",
                FileName = "TSIC-Export-Error.txt"
            };
        }

        var (contentType, fileName) = exportFormat switch
        {
            (int)ReportExportFormat.Pdf => ("application/pdf", "TSIC-Export.pdf"),
            (int)ReportExportFormat.Rtf => ("application/rtf", "TSIC-Export.rtf"),
            (int)ReportExportFormat.Xls => ("application/ms-excel", "TSIC-Export.xls"),
            _ => ("application/pdf", "TSIC-Export.pdf")
        };

        var fileBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        return new ReportExportResult
        {
            FileBytes = fileBytes,
            ContentType = contentType,
            FileName = fileName
        };
    }

    public async Task<ReportExportResult> ExportStoredProcedureToExcelAsync(
        string spName,
        Guid jobId,
        bool useJobId,
        bool useDateUnscheduled = false,
        CancellationToken cancellationToken = default)
    {
        var (reader, connection) = await _reportingRepository.ExecuteStoredProcedureAsync(
            spName, jobId, useJobId, useDateUnscheduled, cancellationToken);

        try
        {
            var reportBytes = await BuildExcelFromDataReader(reader);
            return new ReportExportResult
            {
                FileBytes = reportBytes,
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                FileName = "TSICExcelExport.xlsx"
            };
        }
        finally
        {
            await reader.CloseAsync();
            await connection.CloseAsync();
        }
    }

    public async Task<ReportExportResult> ExportMonthlyReconciliationAsync(
        int settlementMonth,
        int settlementYear,
        bool isMerchandise,
        CancellationToken cancellationToken = default)
    {
        var (reader, connection) = await _reportingRepository.ExecuteMonthlyReconciliationAsync(
            settlementMonth, settlementYear, isMerchandise, cancellationToken);

        try
        {
            var reportBytes = await BuildExcelFromDataReader(reader);
            return new ReportExportResult
            {
                FileBytes = reportBytes,
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                FileName = "TSICExcelExport.xlsx"
            };
        }
        finally
        {
            await reader.CloseAsync();
            await connection.CloseAsync();
        }
    }

    public async Task<ReportExportResult> ExportScheduleToICalAsync(
        List<int> gameIds,
        CancellationToken cancellationToken = default)
    {
        var games = await _reportingRepository.GetScheduleGamesForICalAsync(gameIds, cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("VERSION:2.0");
        sb.AppendLine("PRODID:-//TSIC//Schedule//EN");

        foreach (var game in games)
        {
            if (game.GDate == null) continue;

            sb.AppendLine("BEGIN:VEVENT");
            sb.AppendLine($"DTSTART:{game.GDate.Value:yyyyMMddTHHmmss}");
            sb.AppendLine($"DTEND:{game.GDate.Value.AddHours(1):yyyyMMddTHHmmss}");
            sb.AppendLine($"SUMMARY:{game.T1Name} vs {game.T2Name}");
            sb.AppendLine(string.Format("LOCATION:{0}",
                string.Join(", ", new[] { game.FieldName, game.Address, game.City, game.State, game.Zip }
                    .Where(s => !string.IsNullOrEmpty(s)))));
            sb.AppendLine($"UID:tsic-game-{game.Gid}@teamsportsinfo.com");
            sb.AppendLine("END:VEVENT");
        }

        sb.AppendLine("END:VCALENDAR");

        return new ReportExportResult
        {
            FileBytes = Encoding.UTF8.GetBytes(sb.ToString()),
            ContentType = "text/calendar",
            FileName = "TSIC-SCHEDULE.ics"
        };
    }

    public async Task<bool> BuildLastMonthsJobInvoicesAsync(CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("CrystalReports");
        var crUrl = $"{_settings.CrystalReportsBaseUrl}CrystalReports/BuildLastMonthsJobInvoices";

        var response = await client.GetAsync(crUrl, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task RecordExportHistoryAsync(
        Guid? regId,
        string? storedProcedureName,
        string? reportName,
        CancellationToken cancellationToken = default)
    {
        if (regId == null || regId == Guid.Empty)
        {
            return;
        }

        await _reportingRepository.RecordExportHistoryAsync(
            regId.Value, storedProcedureName, reportName, cancellationToken);
    }

    /// <summary>
    /// Builds an Excel file from a DbDataReader, supporting multiple result sets.
    /// Ported from legacy ReportingService.BuildExcelExport.
    /// </summary>
    private static async Task<byte[]> BuildExcelFromDataReader(DbDataReader reader)
    {
        ExcelPackage.License.SetNonCommercialPersonal("Todd Greenwald");

        using var package = new ExcelPackage();
        ExcelWorksheet? worksheet = null;

        if (reader.HasRows)
        {
            do
            {
                // Check if this result set is a worksheet name marker (single column with "QA Test:" prefix)
                var schema = await reader.GetColumnSchemaAsync();
                if (schema.Count == 1)
                {
                    await reader.ReadAsync();
                    var value = reader.GetValue(0).ToString() ?? string.Empty;

                    if (value.StartsWith("QA Test:"))
                    {
                        var sheetName = value.Split(':')[1].Trim();
                        worksheet = package.Workbook.Worksheets.Add(sheetName);
                        await reader.NextResultAsync();
                    }
                }

                // Ensure we have a worksheet
                worksheet ??= package.Workbook.Worksheets.Add("SearchResults");

                var rowCounter = 0;
                while (await reader.ReadAsync())
                {
                    // Write headers on first row
                    if (rowCounter == 0)
                    {
                        var headerSchema = await reader.GetColumnSchemaAsync();
                        var headerIndex = 0;
                        foreach (var column in headerSchema)
                        {
                            worksheet.Cells[1, headerIndex + 1].Value = column.ColumnName;
                            headerIndex++;
                        }
                    }

                    // Write data rows
                    var dataSchema = await reader.GetColumnSchemaAsync();
                    for (var col = 0; col < dataSchema.Count; col++)
                    {
                        var cellValue = reader.GetValue(col);

                        if (cellValue is DateTime)
                        {
                            worksheet.Cells[rowCounter + 2, col + 1].Value = cellValue;
                            worksheet.Cells[rowCounter + 2, col + 1].Style.Numberformat.Format = "mm/dd/yyyy";
                        }
                        else
                        {
                            worksheet.Cells[rowCounter + 2, col + 1].Value = cellValue == DBNull.Value ? null : cellValue;
                        }
                    }

                    rowCounter++;
                }
            } while (await reader.NextResultAsync());
        }

        // Ensure at least one worksheet exists
        if (package.Workbook.Worksheets.Count == 0)
        {
            package.Workbook.Worksheets.Add("SearchResults");
        }

        return await package.GetAsByteArrayAsync();
    }
}
