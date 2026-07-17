using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BoldReports.Web;
using BoldReports.Writer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Syncfusion.XlsIO;
using TSIC.API.Configuration;
using TSIC.API.Utilities;
using TSIC.Contracts.Configuration;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Reporting;

public sealed class ReportingService : IReportingService
{
    private readonly IReportingRepository _reportingRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ReportingSettings _settings;
    private readonly FileStorageOptions _fileStorage;
    private readonly ILogger<ReportingService> _logger;

    private static readonly JsonSerializerOptions MonthEndJsonOptions = new(JsonSerializerDefaults.Web);

    public ReportingService(
        IReportingRepository reportingRepository,
        IHttpClientFactory httpClientFactory,
        IHostEnvironment hostEnvironment,
        IOptions<ReportingSettings> settings,
        IOptions<FileStorageOptions> fileStorage,
        ILogger<ReportingService> logger)
    {
        _reportingRepository = reportingRepository;
        _httpClientFactory = httpClientFactory;
        _hostEnvironment = hostEnvironment;
        _settings = settings.Value;
        _fileStorage = fileStorage.Value;
        _logger = logger;
    }

    public Task<List<JobReportEntryDto>> GetJobReportsAsync(
        Guid jobId,
        IReadOnlyCollection<string> roleIds,
        CancellationToken cancellationToken = default)
        => _reportingRepository.GetJobReportsAsync(jobId, roleIds, cancellationToken);

    public Task<List<JobReportEntryDto>> GetAllJobReportsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
        => _reportingRepository.GetAllActiveJobReportsAsync(jobId, cancellationToken);

    public Task<bool> HasStoredProcedureEntitlementAsync(
        Guid jobId,
        IReadOnlyCollection<string> roleIds,
        string spName,
        CancellationToken cancellationToken = default)
        => _reportingRepository.HasStoredProcedureEntitlementAsync(jobId, roleIds, spName, cancellationToken);

    public Task<bool> HasStoredProcedureEntitlementAnyRoleAsync(
        Guid jobId,
        string spName,
        CancellationToken cancellationToken = default)
        => _reportingRepository.HasStoredProcedureEntitlementAnyRoleAsync(jobId, spName, cancellationToken);

    public Task<bool> HasBoldReportEntitlementAsync(
        Guid jobId,
        IReadOnlyCollection<string> roleIds,
        string reportName,
        CancellationToken cancellationToken = default)
        => _reportingRepository.HasBoldReportEntitlementAsync(jobId, roleIds, reportName, cancellationToken);

    public Task<bool> HasBoldReportEntitlementAnyRoleAsync(
        Guid jobId,
        string reportName,
        CancellationToken cancellationToken = default)
        => _reportingRepository.HasBoldReportEntitlementAnyRoleAsync(jobId, reportName, cancellationToken);

    // ── SuperUser editor ──

    public Task<List<JobReportEditorRoleDto>> GetEditorRolesAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
        => _reportingRepository.GetEditorRolesAsync(jobId, cancellationToken);

    public Task<List<JobReportEditorRowDto>> GetEditorRowsAsync(
        Guid jobId,
        string roleId,
        CancellationToken cancellationToken = default)
        => _reportingRepository.GetEditorRowsAsync(jobId, roleId, cancellationToken);

    public async Task<JobReportEditorRowDto?> UpdateEditorRowAsync(
        Guid jobReportId,
        Guid jobIdGuard,
        JobReportEditorUpdateDto dto,
        string lebUserId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _reportingRepository.GetJobReportForUpdateAsync(jobReportId, cancellationToken);
        if (entity == null) return null;

        // Defense in depth: row must belong to the caller's current job. Without this,
        // a tampered request could mutate rows in jobs the SU isn't currently scoped to.
        if (entity.JobId != jobIdGuard) return null;

        entity.Title = dto.Title;
        entity.IconName = dto.IconName;
        entity.GroupLabel = dto.GroupLabel;
        entity.SortOrder = dto.SortOrder;
        entity.Active = dto.Active;
        entity.Modified = DateTime.Now;
        entity.LebUserId = lebUserId;

        await _reportingRepository.SaveChangesAsync(cancellationToken);

        return new JobReportEditorRowDto
        {
            JobReportId = entity.JobReportId,
            Title = entity.Title,
            IconName = entity.IconName,
            Controller = entity.Controller,
            Action = entity.Action,
            Kind = entity.Kind,
            GroupLabel = entity.GroupLabel,
            SortOrder = entity.SortOrder,
            Active = entity.Active,
            Modified = entity.Modified,
            LebUserId = entity.LebUserId,
        };
    }

    public async Task<(JobReportEditorRowDto? Row, bool Conflict)> CreateEditorRowAsync(
        Guid jobIdGuard,
        JobReportEditorCreateDto dto,
        string lebUserId,
        CancellationToken cancellationToken = default)
    {
        var entity = new JobReports
        {
            JobReportId = Guid.NewGuid(),
            JobId = jobIdGuard,
            RoleId = dto.RoleId,
            Title = dto.Title,
            IconName = dto.IconName,
            Controller = dto.Controller,
            Action = dto.Action,
            Kind = dto.Kind,
            GroupLabel = dto.GroupLabel,
            SortOrder = dto.SortOrder,
            Active = dto.Active,
            Modified = DateTime.Now,
            LebUserId = lebUserId,
        };

        try
        {
            await _reportingRepository.AddJobReportAsync(entity, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueKeyViolation(ex))
        {
            return (null, true);
        }

        var row = new JobReportEditorRowDto
        {
            JobReportId = entity.JobReportId,
            Title = entity.Title,
            IconName = entity.IconName,
            Controller = entity.Controller,
            Action = entity.Action,
            Kind = entity.Kind,
            GroupLabel = entity.GroupLabel,
            SortOrder = entity.SortOrder,
            Active = entity.Active,
            Modified = entity.Modified,
            LebUserId = entity.LebUserId,
        };
        return (row, false);
    }

    // SQL Server: 2627 = unique constraint, 2601 = unique index
    private static bool IsUniqueKeyViolation(DbUpdateException ex)
        => ex.InnerException is SqlException sql && (sql.Number == 2627 || sql.Number == 2601);

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
            // Crystal Reports' Xls export emits OOXML (.xlsx) bytes despite the enum name.
            // Labelling as .xls + ms-excel MIME triggered Excel's content-vs-extension warning.
            (int)ReportExportFormat.Xls => (
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "TSIC-Export.xlsx"),
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

    public async Task<ReportExportResult> ExportBoldReportAsync(
        string reportName,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // RDL files ship under TSIC.API/Reports/ and copy to the publish output, so
        // ContentRootPath/Reports is the canonical lookup. reportName arrives as the
        // file stem ("TournamentRosterPacked"), never a path — sanitize defensively.
        var safeName = Path.GetFileNameWithoutExtension(reportName);
        var rdlPath = Path.Combine(_hostEnvironment.ContentRootPath, "Reports", $"{safeName}.rdl");
        if (!File.Exists(rdlPath))
        {
            throw new FileNotFoundException($"Bold Reports RDL not found: {safeName}.rdl", rdlPath);
        }

        // Resolve the embedded stored procedure from the RDL — every Bold report
        // we ship currently has a single dataset named MainReportData whose
        // CommandText is the SP to execute. Parsing the RDL keeps Bold + SP in
        // lockstep (rename the SP in the RDL, the service follows automatically).
        var spName = ExtractMainReportSpName(rdlPath)
            ?? throw new InvalidOperationException(
                $"RDL {safeName}.rdl missing MainReportData dataset CommandText");

        // Run the SP via the same path Excel exports use — DataReader → DataTable
        // named to match the RDL DataSet, then feed Bold via ReportDataSourceCollection
        // so it bypasses the RDL's embedded ConnectString (which assumes integrated
        // security from the developer workstation, not the IIS app pool identity).
        var (reader, connection) = await _reportingRepository.ExecuteStoredProcedureAsync(
            spName, jobId, useJobId: true, cancellationToken: cancellationToken);

        DataTable mainData;
        try
        {
            mainData = new DataTable("MainReportData");
            mainData.Load(reader);
        }
        finally
        {
            await reader.CloseAsync();
            await connection.CloseAsync();
        }

        await using var rdlStream = File.OpenRead(rdlPath);
        var dataSources = new ReportDataSourceCollection
        {
            new ReportDataSource { Name = "MainReportData", Value = mainData },
        };

        using var writer = new ReportWriter(rdlStream, dataSources);
        using var output = new MemoryStream();
        writer.Save(output, WriterFormat.PDF);

        return new ReportExportResult
        {
            FileBytes = output.ToArray(),
            ContentType = "application/pdf",
            FileName = $"{safeName}.pdf",
        };
    }

    /// <summary>
    /// Pulls the CommandText from the first DataSet named MainReportData inside the
    /// RDL. Returned value is the bare SP name (schema-qualified or not) — caller
    /// passes it straight to ExecuteStoredProcedureAsync.
    /// </summary>
    private static string? ExtractMainReportSpName(string rdlPath)
    {
        var doc = System.Xml.Linq.XDocument.Load(rdlPath);
        var ns = doc.Root?.GetDefaultNamespace() ?? System.Xml.Linq.XNamespace.None;
        var dataset = doc.Descendants(ns + "DataSet")
            .FirstOrDefault(d => (string?)d.Attribute("Name") == "MainReportData");
        return dataset?.Element(ns + "Query")?.Element(ns + "CommandText")?.Value?.Trim();
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

    // Persisted artifact filenames within a month's folder. meta.json is written LAST and its presence
    // is the "built and complete" signal — a reader that sees meta.json can trust zip + ledger are whole.
    private const string BundleFileName = "bundle.zip";
    private const string LedgerFileName = "ledger.json";
    private const string MetaFileName = "meta.json";

    /// <summary>
    /// Step 3 (files): return the persisted month-end .zip. Builds it once (running the sprocs) if the
    /// month hasn't been prepared yet; otherwise streams straight off disk with no sproc execution.
    /// </summary>
    public async Task<ReconciliationBundleResult> ExportMonthEndCloseBundleAsync(
        int settlementMonth,
        int settlementYear,
        CancellationToken cancellationToken = default)
    {
        var info = await EnsureBuiltAsync(settlementMonth, settlementYear, cancellationToken);
        var dir = ResolveMonthEndDir(settlementMonth, settlementYear);
        var zipBytes = await File.ReadAllBytesAsync(Path.Combine(dir, BundleFileName), cancellationToken);

        return new ReconciliationBundleResult
        {
            Zip = new ReportExportResult
            {
                FileBytes = zipBytes,
                ContentType = "application/zip",
                FileName = $"TSIC-AdnReconciliation-{settlementYear}-{settlementMonth:D2}.zip",
            },
            RegSourceTrnsCount = info.RegSourceTrnsCount,
            RegConsolidatedTrnsCount = info.RegConsolidatedTrnsCount,
            MerchSourceTrnsCount = info.MerchSourceTrnsCount,
            MerchConsolidatedTrnsCount = info.MerchConsolidatedTrnsCount,
        };
    }

    /// <summary>
    /// Step 2 (present): return the persisted human-readable ledger. Builds once if needed, otherwise
    /// deserializes ledger.json off disk — no sproc execution on revisits.
    /// </summary>
    public async Task<MonthEndLedger> GetMonthEndLedgerAsync(
        int settlementMonth,
        int settlementYear,
        CancellationToken cancellationToken = default)
    {
        await EnsureBuiltAsync(settlementMonth, settlementYear, cancellationToken);
        var dir = ResolveMonthEndDir(settlementMonth, settlementYear);
        var json = await File.ReadAllTextAsync(Path.Combine(dir, LedgerFileName), cancellationToken);
        return JsonSerializer.Deserialize<MonthEndLedger>(json, MonthEndJsonOptions)
            ?? new MonthEndLedger { SettlementMonth = settlementMonth, SettlementYear = settlementYear, Tabs = Array.Empty<LedgerTab>() };
    }

    /// <summary>
    /// Eager build: run BOTH reconciliation sprocs ONCE, project the shared sheet model into the ledger,
    /// the two consolidated .iif files, their backing .xlsx, and a flattened human-readable summary
    /// workbook; persist the .zip + ledger.json + meta.json to the month folder. Every later Step-2/Step-3
    /// read is served from those files, so the sprocs (and the reg sproc's write) run once per pull.
    /// </summary>
    public async Task<MonthEndArtifactsInfo> BuildAndPersistMonthEndAsync(
        int settlementMonth,
        int settlementYear,
        CancellationToken cancellationToken = default)
    {
        // Sequential (never parallel) — both share the one scoped DbContext connection.
        var regSheets = await RunStackSheetsAsync(settlementMonth, settlementYear, isMerchandise: false, cancellationToken);
        var merchSheets = await RunStackSheetsAsync(settlementMonth, settlementYear, isMerchandise: true, cancellationToken);

        var reg = BuildStackArtifacts(regSheets);
        var merch = BuildStackArtifacts(merchSheets);

        var tabs = new List<LedgerTab>();
        foreach (var sheet in regSheets) tabs.Add(ParseSheetToLedgerTab(sheet, "Registration"));
        foreach (var sheet in merchSheets) tabs.Add(ParseSheetToLedgerTab(sheet, "Merch"));

        var monthKey = $"{settlementYear}-{settlementMonth:D2}";
        var summaryXlsx = BuildFlattenedSummaryXlsx(tabs);

        // Non-merch and merch reconcile to DIFFERENT QuickBooks customers even inside the same company
        // file, and staff inspect each ledger on its own — so the close ships two independent .iif files
        // (+ backing .xlsx). The flattened summary is the on-screen ledger for offline review.
        var zipEntries = new List<(string, byte[])>
        {
            ($"TSIC-AdnReconciliation-Reg-{monthKey}.xlsx", reg.Xlsx),
            ($"TSIC-AdnReconciliation-Merch-{monthKey}.xlsx", merch.Xlsx),
            ($"TSIC-AdnReconciliation-Summary-{monthKey}.xlsx", summaryXlsx),
            ("reg-consolodated.iif", reg.Iif),
            ("merch-consolodated.iif", merch.Iif),
        };
        var zipBytes = BuildReconciliationZip(zipEntries.ToArray());

        var ledger = new MonthEndLedger
        {
            SettlementMonth = settlementMonth,
            SettlementYear = settlementYear,
            Tabs = tabs,
        };

        var info = new MonthEndArtifactsInfo
        {
            SettlementMonth = settlementMonth,
            SettlementYear = settlementYear,
            BuiltAt = DateTime.Now,
            LedgerTabCount = tabs.Count,
            RegSourceTrnsCount = reg.SourceTrns,
            RegConsolidatedTrnsCount = reg.ConsolidatedTrns,
            MerchSourceTrnsCount = merch.SourceTrns,
            MerchConsolidatedTrnsCount = merch.ConsolidatedTrns,
        };

        await PersistMonthEndAsync(settlementMonth, settlementYear, zipBytes, ledger, info, cancellationToken);
        return info;
    }

    /// <summary>Drops a month's persisted artifacts so the next read rebuilds — called after a re-download.</summary>
    public void InvalidateMonthEnd(int settlementMonth, int settlementYear)
    {
        var dir = ResolveMonthEndDir(settlementMonth, settlementYear);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Builds + persists the month if meta.json (the completeness marker) is absent; returns its info either way.</summary>
    private async Task<MonthEndArtifactsInfo> EnsureBuiltAsync(
        int settlementMonth,
        int settlementYear,
        CancellationToken cancellationToken)
    {
        var dir = ResolveMonthEndDir(settlementMonth, settlementYear);
        var metaPath = Path.Combine(dir, MetaFileName);
        var bundlePath = Path.Combine(dir, BundleFileName);
        var ledgerPath = Path.Combine(dir, LedgerFileName);

        if (File.Exists(metaPath) && File.Exists(bundlePath) && File.Exists(ledgerPath))
        {
            var existing = JsonSerializer.Deserialize<MonthEndArtifactsInfo>(
                await File.ReadAllTextAsync(metaPath, cancellationToken), MonthEndJsonOptions);
            if (existing != null) return existing;
        }

        return await BuildAndPersistMonthEndAsync(settlementMonth, settlementYear, cancellationToken);
    }

    private async Task PersistMonthEndAsync(
        int settlementMonth,
        int settlementYear,
        byte[] zipBytes,
        MonthEndLedger ledger,
        MonthEndArtifactsInfo info,
        CancellationToken cancellationToken)
    {
        var dir = ResolveMonthEndDir(settlementMonth, settlementYear);
        Directory.CreateDirectory(dir);

        var metaPath = Path.Combine(dir, MetaFileName);
        // Clear the completeness marker first so a crash mid-write can't leave a half-built month
        // looking ready. meta.json is (re)written last, after zip + ledger are safely in place.
        if (File.Exists(metaPath)) File.Delete(metaPath);

        await WriteAtomicAsync(Path.Combine(dir, BundleFileName), zipBytes, cancellationToken);
        await WriteAtomicAsync(
            Path.Combine(dir, LedgerFileName),
            JsonSerializer.SerializeToUtf8Bytes(ledger, MonthEndJsonOptions),
            cancellationToken);
        await WriteAtomicAsync(
            metaPath,
            JsonSerializer.SerializeToUtf8Bytes(info, MonthEndJsonOptions),
            cancellationToken);
    }

    private static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var tmp = path + ".tmp";
        await File.WriteAllBytesAsync(tmp, bytes, cancellationToken);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Resolves the month's artifact folder under the configured export path (relative values resolve
    /// against ContentRoot; default <c>{ContentRoot}/App_Data/AdnMonthEnd</c>). The leaf is built from
    /// validated int month/year only — never request strings — so there is no path-traversal surface.
    /// </summary>
    private string ResolveMonthEndDir(int settlementMonth, int settlementYear)
    {
        var configured = _fileStorage.MonthEndExportPath;
        var basePath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(_hostEnvironment.ContentRootPath, "App_Data", "AdnMonthEnd")
            : Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(_hostEnvironment.ContentRootPath, configured);

        return Path.Combine(basePath, $"{settlementYear:D4}-{settlementMonth:D2}");
    }

    private async Task<List<SheetData>> RunStackSheetsAsync(
        int settlementMonth,
        int settlementYear,
        bool isMerchandise,
        CancellationToken cancellationToken)
    {
        var (reader, connection) = await _reportingRepository.ExecuteMonthlyReconciliationAsync(
            settlementMonth, settlementYear, isMerchandise, cancellationToken);
        try
        {
            return await ReadReaderIntoSheetsAsync(reader);
        }
        finally
        {
            await reader.CloseAsync();
            await connection.CloseAsync();
        }
    }

    /// <summary>
    /// Projects one stack's already-read sheet model into both artifacts — the backing .xlsx and the
    /// consolidated .iif — so the QuickBooks IIF can never drift from the Excel it was reconciled against.
    /// </summary>
    private static StackArtifacts BuildStackArtifacts(List<SheetData> sheets)
    {
        var (iifBytes, sourceTrns, consolidatedTrns) = BuildConsolidatedIif(sheets);
        return new StackArtifacts(BuildExcelFromSheets(sheets), iifBytes, sourceTrns, consolidatedTrns);
    }

    private readonly record struct StackArtifacts(byte[] Xlsx, byte[] Iif, int SourceTrns, int ConsolidatedTrns);

    /// <summary>
    /// Renders the flattened, human-readable ledger (the on-screen Step-2 view) to an .xlsx for the zip:
    /// transaction tabs collapse each double-entry group to one line with its splits indented beneath;
    /// QA tabs pass through. Sheet names are prefixed R/M and made Excel-safe (unique, ≤31 chars).
    /// </summary>
    private static byte[] BuildFlattenedSummaryXlsx(IReadOnlyList<LedgerTab> tabs)
    {
        var sheets = new List<SheetData>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tab in tabs)
        {
            var prefix = tab.Stack switch { "Merch" => "M ", "Returns" => "Ret ", _ => "R " };
            var sheet = new SheetData { Name = ToUniqueSheetName(prefix + tab.Name, usedNames) };

            if (tab.Kind == "transactions")
            {
                sheet.Columns.AddRange(new[] { "Date", "Type", "Client", "Account", "Amount", "Doc", "Memo" });
                foreach (var e in tab.Entries)
                {
                    sheet.Rows.Add(new object?[] { e.Date, e.Type, e.Party, e.Account, e.Amount, e.DocNum, e.Memo });
                    foreach (var sp in e.Splits)
                    {
                        sheet.Rows.Add(new object?[] { "", "", sp.Party, "    ↳ " + sp.Account, sp.Amount, "", sp.Memo });
                    }
                }
            }
            else
            {
                sheet.Columns.AddRange(tab.Columns);
                foreach (var row in tab.Rows)
                {
                    sheet.Rows.Add(row.Cast<object?>().ToArray());
                }
            }

            sheets.Add(sheet);
        }

        if (sheets.Count == 0)
        {
            sheets.Add(new SheetData { Name = "Summary" });
        }

        return BuildExcelFromSheets(sheets);
    }

    private static string ToUniqueSheetName(string raw, HashSet<string> used)
    {
        // Excel sheet names: ≤31 chars, none of : \ / ? * [ ].
        var cleaned = new string(raw.Select(c => ":\\/?*[]".Contains(c) ? '-' : c).ToArray());
        if (cleaned.Length > 31) cleaned = cleaned[..31];
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "Sheet";

        var candidate = cleaned;
        var suffix = 2;
        while (!used.Add(candidate))
        {
            var tag = $" ({suffix++})";
            candidate = cleaned.Length + tag.Length > 31 ? cleaned[..(31 - tag.Length)] + tag : cleaned + tag;
        }
        return candidate;
    }

    /// <summary>
    /// Turns one sproc sheet into a ledger tab. A sheet whose first column is a <c>!</c>-prefixed IIF
    /// header is a double-entry sheet → flatten each <c>TRNS</c>+<c>SPL</c>…<c>ENDTRNS</c> group into one
    /// entry (amount = the TRNS side; splits attached). Otherwise it's a QA sheet → pass through as a table.
    /// </summary>
    private static LedgerTab ParseSheetToLedgerTab(SheetData sheet, string stack)
    {
        var isIif = sheet.Columns.Count > 0 && sheet.Columns[0].TrimStart().StartsWith('!');

        if (!isIif)
        {
            var rows = sheet.Rows
                .Select(r => (IReadOnlyList<string>)r.Select(CellToDisplay).ToList())
                .ToList();

            return new LedgerTab
            {
                Name = sheet.Name,
                Stack = stack,
                Kind = "table",
                Columns = sheet.Columns.ToList(),
                Rows = rows,
                Entries = Array.Empty<LedgerEntry>(),
            };
        }

        var map = IifColumnMap.From(sheet.Columns);
        var entries = new List<LedgerEntry>();

        string date = "", type = "", party = "", account = "", docNum = "", memo = "";
        decimal amount = 0m;
        var splits = new List<LedgerSplit>();
        var open = false;

        void Flush()
        {
            if (!open) return;
            entries.Add(new LedgerEntry
            {
                Date = date,
                Type = type,
                Party = party,
                Account = account,
                Amount = amount,
                DocNum = docNum,
                Memo = memo,
                Splits = splits,
            });
            open = false;
            splits = new List<LedgerSplit>();
        }

        foreach (var row in sheet.Rows)
        {
            var keyword = (row.Length > 0 ? row[0]?.ToString() : null)?.Trim() ?? string.Empty;

            // Skip the !TRNS / !SPL / !ENDTRNS column-definition rows the sproc emits inline.
            if (keyword.StartsWith('!'))
            {
                continue;
            }

            switch (keyword)
            {
                case "TRNS":
                    Flush();
                    open = true;
                    date = map.Cell(row, map.Date);
                    type = map.Cell(row, map.Type);
                    party = map.Cell(row, map.Name);
                    account = map.Cell(row, map.Account);
                    amount = map.Amount >= 0 ? ParseAmount(map.Cell(row, map.Amount)) : 0m;
                    docNum = map.Cell(row, map.DocNum);
                    memo = map.Cell(row, map.Memo);
                    break;

                case "SPL":
                    if (open)
                    {
                        splits.Add(new LedgerSplit
                        {
                            Account = map.Cell(row, map.Account),
                            Party = map.Cell(row, map.Name),
                            Amount = map.Amount >= 0 ? ParseAmount(map.Cell(row, map.Amount)) : 0m,
                            Memo = map.Cell(row, map.Memo),
                        });
                    }
                    break;

                case "ENDTRNS":
                    Flush();
                    break;
            }
        }

        Flush();

        return new LedgerTab
        {
            Name = sheet.Name,
            Stack = stack,
            Kind = "transactions",
            Columns = Array.Empty<string>(),
            Rows = Array.Empty<IReadOnlyList<string>>(),
            Entries = entries,
        };
    }

    private static string CellToDisplay(object? value) => value switch
    {
        null => string.Empty,
        DateTime dt => dt.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static decimal ParseAmount(string value)
        => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    /// <summary>
    /// Column positions for an IIF sheet, resolved by header name (leading <c>!</c> stripped) so the
    /// parser follows the sproc if columns shift. -1 means the column is absent on that sheet.
    /// </summary>
    private readonly record struct IifColumnMap(int Date, int Type, int Account, int Name, int Amount, int DocNum, int Memo)
    {
        public static IifColumnMap From(IReadOnlyList<string> columns)
        {
            int Find(string name)
            {
                for (var i = 0; i < columns.Count; i++)
                {
                    if (string.Equals(columns[i].TrimStart('!').Trim(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
                return -1;
            }

            return new IifColumnMap(
                Date: Find("DATE"),
                Type: Find("TRNSTYPE"),
                Account: Find("ACCNT"),
                Name: Find("NAME"),
                Amount: Find("AMOUNT"),
                DocNum: Find("DOCNUM"),
                Memo: Find("MEMO"));
        }

        public string Cell(object?[] row, int index)
            => (index >= 0 && index < row.Length ? row[index]?.ToString() : null)?.Trim() ?? string.Empty;
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
        => BuildExcelFromSheets(await ReadReaderIntoSheetsAsync(reader));

    /// <summary>
    /// One in-memory result set from a multi-sheet stored procedure: the sheet name (from the
    /// "QA Test:" marker or the legacy default), the column names, and the raw row values.
    /// Both the Excel renderer and the QuickBooks IIF renderer project from this single model,
    /// so a reconciliation SP is executed exactly once yet yields two consistent artifacts.
    /// </summary>
    private sealed class SheetData
    {
        public required string Name { get; init; }
        public List<string> Columns { get; } = new();
        public List<object?[]> Rows { get; } = new();
    }

    /// <summary>
    /// Drains a (possibly multi-result-set) reader into a list of <see cref="SheetData"/>.
    /// Mirrors the legacy BuildExcelFromDataReader traversal exactly: a single-column result set
    /// whose first row is <c>"QA Test: &lt;name&gt;"</c> renames the *next* result set's sheet;
    /// otherwise rows accumulate into a "SearchResults" sheet. DBNull is normalized to null.
    /// </summary>
    private static async Task<List<SheetData>> ReadReaderIntoSheetsAsync(DbDataReader reader)
    {
        var sheets = new List<SheetData>();
        SheetData? current = null;

        SheetData AddSheet(string name)
        {
            var sheet = new SheetData { Name = name };
            sheets.Add(sheet);
            return sheet;
        }

        if (reader.HasRows)
        {
            do
            {
                // A single-column result set with a "QA Test:" first row names the next sheet.
                if (reader.FieldCount == 1)
                {
                    await reader.ReadAsync();
                    var value = reader.GetValue(0).ToString() ?? string.Empty;

                    if (value.StartsWith("QA Test:"))
                    {
                        var sheetName = value.Split(':')[1].Trim();
                        current = AddSheet(sheetName);
                        await reader.NextResultAsync();
                    }
                }

                current ??= AddSheet("SearchResults");

                var rowCounter = 0;
                while (await reader.ReadAsync())
                {
                    if (rowCounter == 0 && current.Columns.Count == 0)
                    {
                        for (var col = 0; col < reader.FieldCount; col++)
                        {
                            current.Columns.Add(reader.GetName(col));
                        }
                    }

                    var row = new object?[reader.FieldCount];
                    for (var col = 0; col < reader.FieldCount; col++)
                    {
                        var cellValue = reader.GetValue(col);
                        row[col] = cellValue == DBNull.Value ? null : cellValue;
                    }
                    current.Rows.Add(row);
                    rowCounter++;
                }
            } while (await reader.NextResultAsync());
        }

        if (sheets.Count == 0)
        {
            AddSheet("SearchResults");
        }

        return sheets;
    }

    /// <summary>
    /// Renders the sheet model to an .xlsx. Behavior-preserving port of the legacy
    /// BuildExcelFromDataReader Excel path (reuse default sheet, header row, DateTime formatting).
    /// </summary>
    private static byte[] BuildExcelFromSheets(List<SheetData> sheets)
    {
        using var excelEngine = new ExcelEngine();
        IApplication application = excelEngine.Excel;
        application.DefaultVersion = Syncfusion.XlsIO.ExcelVersion.Xlsx;
        IWorkbook workbook = application.Workbooks.Create(1);
        var sheetsCreated = 0;

        // Reuse the default sheet XlsIO creates for the first sheet, then create new
        // ones — preserves EPPlus's "start empty, add named sheets" behavior without
        // leaving a stray "Sheet1" in the output.
        IWorksheet AddWorksheet(string name)
        {
            var sheet = sheetsCreated == 0 ? workbook.Worksheets[0] : workbook.Worksheets.Create(name);
            sheet.Name = name;
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

    // Consolidation order for QuickBooks IIF sheets — ported verbatim from scripts/adn/IIFExtract.ps1.
    // A sheet's position is the index of the first keyword its name contains; unmatched sheets sort last.
    private static readonly string[] IifKeywordOrder =
    {
        "Payments", "Credits", "TSIC-Fees", "MERCH-Fees", "CC-Fees", "Admin-Fees", "Retainers", "Checks",
    };

    /// <summary>
    /// Builds the single consolidated QuickBooks .iif from the sheet model — the server-side
    /// equivalent of the manual Excel-COM + <c>scripts/adn/IIFExtract.ps1</c> pipeline. Keeps only
    /// sheets that carry BOTH a <c>!</c>-prefixed IIF header line and at least one data line, orders
    /// them by <see cref="IifKeywordOrder"/>, concatenates (dropping blanks and <c>!ENDDATA</c>), and
    /// returns the UTF-8 (BOM, CRLF) bytes plus the source/consolidated TRNS counts for validation.
    /// </summary>
    private static (byte[] Bytes, int SourceTrns, int ConsolidatedTrns) BuildConsolidatedIif(List<SheetData> sheets)
    {
        var candidates = new List<(int SortOrder, List<string> Lines)>();

        foreach (var sheet in sheets)
        {
            var lines = new List<string>();
            if (sheet.Columns.Count > 0)
            {
                lines.Add(string.Join('\t', sheet.Columns));
            }
            foreach (var row in sheet.Rows)
            {
                lines.Add(string.Join('\t', row.Select(CellToIif)));
            }

            // Drop blank lines and the QuickBooks terminator, matching the .ps1 filter.
            var kept = lines
                .Select(l => l.TrimEnd('\r', '\n'))
                .Where(l => !string.IsNullOrWhiteSpace(l) && l != "!ENDDATA")
                .ToList();

            var hasHeader = kept.Any(l => l.StartsWith('!'));
            var hasData = kept.Any(l => !l.StartsWith('!'));
            if (!hasHeader || !hasData)
            {
                continue;
            }

            var sortOrder = IifKeywordOrder.Length;
            for (var k = 0; k < IifKeywordOrder.Length; k++)
            {
                if (sheet.Name.Contains(IifKeywordOrder[k], StringComparison.Ordinal))
                {
                    sortOrder = k;
                    break;
                }
            }

            candidates.Add((sortOrder, kept));
        }

        var consolidated = new List<string>();
        var sourceTrns = 0;
        foreach (var candidate in candidates.OrderBy(c => c.SortOrder))
        {
            sourceTrns += candidate.Lines.Count(l => l.StartsWith("TRNS\t"));
            consolidated.AddRange(candidate.Lines);
        }
        var consolidatedTrns = consolidated.Count(l => l.StartsWith("TRNS\t"));

        var text = string.Join("\r\n", consolidated) + "\r\n";
        var preamble = new UTF8Encoding(true).GetPreamble();
        var body = new UTF8Encoding(false).GetBytes(text);
        var bytes = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);

        return (bytes, sourceTrns, consolidatedTrns);
    }

    private static string CellToIif(object? value) => value?.ToString() ?? string.Empty;

    private static byte[] BuildReconciliationZip(IReadOnlyList<(string Name, byte[] Bytes)> entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(bytes, 0, bytes.Length);
            }
        }
        return ms.ToArray();
    }
}
