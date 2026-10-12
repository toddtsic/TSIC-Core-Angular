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
using TSIC.API.Services.Shared.Files;
using SheetData = TSIC.API.Services.Shared.Files.ExcelSheet;
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

    public Task<bool> HasCrystalActionEntitlementAsync(
        Guid jobId,
        IReadOnlyCollection<string> roleIds,
        string action,
        CancellationToken cancellationToken = default)
        => _reportingRepository.HasCrystalActionEntitlementAsync(jobId, roleIds, action, cancellationToken);

    public Task<bool> HasCrystalActionEntitlementAnyRoleAsync(
        Guid jobId,
        string action,
        CancellationToken cancellationToken = default)
        => _reportingRepository.HasCrystalActionEntitlementAnyRoleAsync(jobId, action, cancellationToken);

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

    // ── Reports LIBRARY ──────────────────────────────────────────────────────

    public async Task<List<ReportLibraryEntryDto>> GetReportLibraryAsync(
        Guid jobId,
        string shelfRoleId,
        bool callerIsSuperuser,
        CancellationToken cancellationToken = default)
    {
        var allowed = ReportLibraryGate.AllowedMinRoleIds(shelfRoleId);
        if (allowed.Count == 0) return new List<ReportLibraryEntryDto>();

        var context = await _reportingRepository.GetShelfContextAsync(jobId, cancellationToken);
        if (context == null) return new List<ReportLibraryEntryDto>();

        return await _reportingRepository.GetReportLibraryForShelfAsync(
            jobId, shelfRoleId, allowed, context.CustomerId, context.JobTypeId,
            bypassApplicability: callerIsSuperuser, cancellationToken);
    }

    public async Task<(JobReportEntryDto? Row, ShelfAddOutcome Outcome)> AddLibraryReportToShelfAsync(
        Guid jobId,
        string shelfRoleId,
        bool callerIsSuperuser,
        Guid reportLibraryId,
        string lebUserId,
        CancellationToken cancellationToken = default)
    {
        var entry = await _reportingRepository.GetReportLibraryEntryAsync(reportLibraryId, cancellationToken);
        if (entry == null) return (null, ShelfAddOutcome.LibraryEntryNotFound);

        var context = await _reportingRepository.GetShelfContextAsync(jobId, cancellationToken);
        if (context == null) return (null, ShelfAddOutcome.LibraryEntryNotFound);

        // THE security boundary: every gate the browse applied, applied again here on the
        // entry as it is in the database now, never on what the client says it saw.
        var reason = ReportLibraryGate.WhyNotAddable(
            entry.MinRoleId,
            entry.OwnerCustomerId,
            entry.JobType.Select(jt => jt.JobTypeId).ToList(),
            shelfRoleId,
            callerIsSuperuser,
            context.CustomerId,
            context.JobTypeId);
        if (reason != null)
        {
            _logger.LogWarning(
                "Reports library add refused: job {JobId} role {RoleId} entry {ReportKey} — {Reason}",
                jobId, shelfRoleId, entry.ReportKey, reason);
            return (null, ShelfAddOutcome.Forbidden);
        }

        var existing = await _reportingRepository.GetShelfRowIdAsync(jobId, shelfRoleId, reportLibraryId, cancellationToken);
        if (existing.HasValue) return (null, ShelfAddOutcome.AlreadyOnShelf);

        var maxSort = await _reportingRepository.GetMaxShelfSortOrderAsync(jobId, shelfRoleId, cancellationToken);

        var entity = new JobReports
        {
            JobReportId = Guid.NewGuid(),
            JobId = jobId,
            RoleId = shelfRoleId,
            Title = entry.Title,
            IconName = entry.IconName,
            Controller = entry.Controller,
            Action = entry.Action,
            Kind = entry.Kind,
            GroupLabel = entry.CategoryCode,
            SortOrder = maxSort + 10,
            Active = true,
            Modified = DateTime.Now,
            LebUserId = lebUserId,
            ReportLibraryId = entry.ReportLibraryId,
        };

        try
        {
            await _reportingRepository.AddJobReportAsync(entity, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueKeyViolation(ex))
        {
            // Two adds raced, or a legacy row with the same (job, role, controller, action,
            // group) tuple exists but was never linked. Either way: it is on the shelf.
            return (null, ShelfAddOutcome.AlreadyOnShelf);
        }

        var row = new JobReportEntryDto
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
            ReportLibraryId = entity.ReportLibraryId,
            Description = entry.Description,
        };
        return (row, ShelfAddOutcome.Added);
    }

    public Task<bool> RemoveFromShelfAsync(
        Guid jobReportId,
        Guid jobId,
        string shelfRoleId,
        CancellationToken cancellationToken = default)
        => _reportingRepository.DeleteShelfRowAsync(jobReportId, jobId, shelfRoleId, cancellationToken);

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

        // A 200 is not proof a report came back. `cr2025` resolves through the wildcard DNS record
        // to the prod box, so whenever the Crystal site is stopped every request falls through to
        // the Angular site, which answers 200 with its index.html for ANY path. Status alone said
        // "success", and ~11 KB of HTML was handed to the user as TSIC-Export.pdf — a download that
        // silently will not open, on every report surface at once.
        //
        // Crystal is being retired (the reports are moving to code-gen off the Syncfusion
        // file-format libraries), so a stopped CR service is the expected steady state, not an
        // outage. Fail loudly on it.
        var upstreamMediaType = response.Content.Headers.ContentType?.MediaType;
        if (upstreamMediaType != null && upstreamMediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "Crystal Reports returned text/html for {ReportName} — the reporting service at {Url} is not serving reports.",
                reportName, crUrl);

            var notServingBytes = Encoding.UTF8.GetBytes(
                "Crystal Reports export failed: the reporting service is not currently serving reports " +
                "(it answered with a web page instead of a document). No report was produced.");

            return new ReportExportResult
            {
                FileBytes = notServingBytes,
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
        // Procs already reproduced in EF keep their catalogue row (and so their spName-keyed
        // entitlement + export history) but never reach SQL Server as a proc call.
        var efSheets = await BuildEfReplacementSheetsAsync(spName, jobId, cancellationToken);
        if (efSheets != null)
        {
            return new ReportExportResult
            {
                FileBytes = ExcelWorkbookWriter.Build(efSheets),
                ContentType = ExcelWorkbookWriter.XlsxContentType,
                FileName = "TSICExcelExport.xlsx"
            };
        }

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

    /// <summary>
    /// Sheets for an export-sp report whose proc has been reproduced in EF, or null when
    /// <paramref name="spName"/> is still served by its stored procedure. Matches the catalogue's
    /// spName with or without brackets.
    /// </summary>
    private async Task<List<SheetData>?> BuildEfReplacementSheetsAsync(
        string spName,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var normalized = spName.Replace("[", string.Empty).Replace("]", string.Empty).Trim();

        if (JobPlayersLayouts.TryGetValue(normalized, out var layout))
        {
            return [await BuildJobPlayersSheetAsync(jobId, layout, cancellationToken)];
        }

        if (normalized.Equals("reporting_migrate.Get_Staff_ForExcelExport", StringComparison.OrdinalIgnoreCase))
        {
            return [await BuildJobStaffSheetAsync(jobId, cancellationToken)];
        }

        if (normalized.Equals("reporting_migrate.Get_Player_Transactions_ForExcelExport", StringComparison.OrdinalIgnoreCase))
        {
            return [await BuildPlayerTransactionsSheetAsync(jobId, cancellationToken)];
        }

        if (normalized.Equals("reporting_migrate.Get_DiscountedPlayers", StringComparison.OrdinalIgnoreCase))
        {
            return [await BuildDiscountedPlayersSheetAsync(jobId, cancellationToken)];
        }

        if (normalized.Equals("reporting_migrate.PlayerStats_ParisiResults", StringComparison.OrdinalIgnoreCase))
        {
            return [await BuildPlayerStatsParisiSheetAsync(jobId, cancellationToken)];
        }

        if (normalized.Equals("reporting_migrate.UsageByJobAndRegistrant", StringComparison.OrdinalIgnoreCase))
        {
            return [await BuildMobileJobUsersSheetAsync(jobId, cancellationToken)];
        }

        if (normalized.Equals("reporting_migrate.League_Teams", StringComparison.OrdinalIgnoreCase))
        {
            return [await BuildLeagueTeamsSheetAsync(jobId, cancellationToken)];
        }

        return null;
    }

    /// <summary>
    /// A sheet from already-fetched rows. The proc path only learns its column names from the
    /// first data row, so a result with no rows exports a headerless sheet; kept identical here.
    /// </summary>
    private static SheetData ToSheet<T>(
        string sheetName,
        IReadOnlyCollection<T> rows,
        string[] columns,
        Func<T, object?[]> toRow)
    {
        var sheet = new SheetData { Name = sheetName };
        if (rows.Count == 0)
        {
            return sheet;
        }

        sheet.WithColumns(columns);
        foreach (var r in rows)
        {
            sheet.Rows.Add(toRow(r));
        }

        return sheet;
    }

    /// <summary>
    /// "Staff" sheet of the "Staff Data (Excel)" export: the proc's 10 columns, same names and order.
    /// </summary>
    private async Task<SheetData> BuildJobStaffSheetAsync(Guid jobId, CancellationToken cancellationToken) =>
        ToSheet(
            "Staff",
            await _reportingRepository.GetJobStaffExcelRowsAsync(jobId, cancellationToken),
            [
                "RegistrationID", "Active?", "FirstName", "LastName", "Email", "Username",
                "Staff", "agegroupName", "teamName", "cellphone",
            ],
            r =>
            [
                r.RegistrationId, r.Active, r.FirstName, r.LastName, r.Email, r.UserName,
                SqlConcat(r.LastName, ", ", r.FirstName), r.AgegroupName, r.TeamName, r.Cellphone,
            ]);

    /// <summary>
    /// "Player Transactions" sheet of the "Player Transactions (Excel)" export: the proc's 20
    /// columns, same names and order. <c>createdate</c> is text, as the proc's
    /// <c>convert(char(19), …, 120)</c> made it.
    /// </summary>
    private async Task<SheetData> BuildPlayerTransactionsSheetAsync(Guid jobId, CancellationToken cancellationToken) =>
        ToSheet(
            "Player Transactions",
            await _reportingRepository.GetPlayerTransactionExcelRowsAsync(jobId, cancellationToken),
            [
                "RegistrationID", "FirstName", "LastName", "gender", "DOB", "agegroupName", "teamName",
                "paid_total", "owed_total", "paymentMethod", "aID", "dueamt", "payamt",
                "adnCC4", "adnCCExpDate", "adnInvoiceNo", "adnTransactionID", "checkNo", "comment", "createdate",
            ],
            r =>
            [
                r.RegistrationId, r.FirstName, r.LastName, r.Gender, r.Dob?.Date, r.AgegroupName, r.TeamName,
                r.PaidTotal, r.OwedTotal, r.PaymentMethod, r.AccountingId, r.DueAmount, r.PayAmount,
                r.AdnCc4, r.AdnCcExpDate, r.AdnInvoiceNo, r.AdnTransactionId, r.CheckNo, r.Comment,
                r.CreateDate?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ]);

    /// <summary>
    /// "Discounted Players" sheet of the "Discounted Players (Excel)" export: the proc's 17
    /// columns, same names and order.
    /// </summary>
    private async Task<SheetData> BuildDiscountedPlayersSheetAsync(Guid jobId, CancellationToken cancellationToken) =>
        ToSheet(
            "Discounted Players",
            await _reportingRepository.GetDiscountedPlayerExcelRowsAsync(jobId, cancellationToken),
            [
                "LastName", "FirstName", "agegroupName", "teamName",
                "Mom_FirstName", "Mom_Email", "Mom_Cellphone", "Mom_LastName",
                "Dad_FirstName", "Dad_LastName", "Dad_Email", "Dad_Cellphone",
                "fee_discount", "codeName", "active", "codeStartDate", "codeEndDate",
            ],
            r =>
            [
                r.LastName, r.FirstName, r.AgegroupName, r.TeamName,
                r.MomFirstName, r.MomEmail, r.MomCellphone, r.MomLastName,
                r.DadFirstName, r.DadLastName, r.DadEmail, r.DadCellphone,
                r.FeeDiscount, r.CodeName, r.CodeActive, r.CodeStartDate, r.CodeEndDate,
            ]);

    /// <summary>
    /// "Parisi Stats" sheet of the "Player Stats Export - Parisi (Excel)" export: the proc's 11
    /// columns, same names and order.
    /// </summary>
    private async Task<SheetData> BuildPlayerStatsParisiSheetAsync(Guid jobId, CancellationToken cancellationToken) =>
        ToSheet(
            "Parisi Stats",
            await _reportingRepository.GetPlayerStatsParisiExcelRowsAsync(jobId, cancellationToken),
            [
                "agegroupName", "FirstName", "LastName", "teamName", "uniform_no", "school_name", "club_name",
                "fastestshot", "five_ten_five", "fourtyyarddash", "threehundredshuttle",
            ],
            r =>
            [
                r.AgegroupName, r.FirstName, r.LastName, r.TeamName, r.UniformNo, r.SchoolName, r.ClubName,
                r.FastestShot, r.FiveTenFive, r.FortyYardDash, r.ThreeHundredShuttle,
            ]);

    /// <summary>
    /// "Mobile Users" sheet of the "Mobile Users (Excel)" export: the proc's 7 columns, same names
    /// and order.
    /// </summary>
    private async Task<SheetData> BuildMobileJobUsersSheetAsync(Guid jobId, CancellationToken cancellationToken) =>
        ToSheet(
            "Mobile Users",
            await _reportingRepository.GetMobileJobUserExcelRowsAsync(jobId, cancellationToken),
            ["customerName", "jobName", "Role", "LastName", "FirstName", "Type", "modified"],
            r => [r.CustomerName, r.JobName, r.Role, r.LastName, r.FirstName, r.DeviceType, r.Modified]);

    /// <summary>
    /// "Teams Export" sheet of the "Teams Export (Excel)" export: the proc's 15 columns, same names
    /// and order. <c>Comment</c> has its carriage returns turned into commas and <c>teamID</c> is
    /// upper-case text, as the proc's <c>replace(…, char(13), ',')</c> and
    /// <c>convert(varchar, uniqueidentifier)</c> made them.
    /// </summary>
    private async Task<SheetData> BuildLeagueTeamsSheetAsync(Guid jobId, CancellationToken cancellationToken) =>
        ToSheet(
            "Teams Export",
            await _reportingRepository.GetLeagueTeamExcelRowsAsync(jobId, cancellationToken),
            [
                "Agegroup", "Division", "Team", "Club", "LastName", "FirstName", "Email", "City",
                "State", "ZIP", "Phone", "Comment", "HomeField", "teamID", "LOP",
            ],
            r =>
            [
                r.AgegroupName, r.DivName, r.TeamName, r.ClubName, r.LastName, r.FirstName, r.Email, r.City,
                r.State, r.PostalCode, r.Cellphone, r.TeamComments?.Replace('\r', ','), r.HomeField,
                r.TeamId.ToString().ToUpperInvariant(), r.LevelOfPlay,
            ]);

    /// <summary>
    /// One layout of the job-players export family: its sheet name, header names in order, and
    /// the projection of a shared row onto those columns.
    /// </summary>
    private sealed record JobPlayersLayout(
        string SheetName,
        string[] Columns,
        Func<JobPlayersExcelRowDto, object?[]> ToRow);

    /// <summary>
    /// The legacy "player data" and "camp" exports were all column subsets of one shared proc —
    /// same joins, filter and order — so they share one query here and differ only in sheet name,
    /// header names and the per-row projection.
    /// </summary>
    private async Task<SheetData> BuildJobPlayersSheetAsync(
        Guid jobId,
        JobPlayersLayout layout,
        CancellationToken cancellationToken)
    {
        var rows = await _reportingRepository.GetJobPlayersExcelRowsAsync(jobId, cancellationToken);
        return ToSheet(layout.SheetName, rows, layout.Columns, layout.ToRow);
    }

    /// <summary>
    /// Every proc of the job-players family that is now served from EF, keyed by its canonical
    /// (unbracketed) name. Column names and order are each proc's own, verbatim.
    /// </summary>
    private static readonly Dictionary<string, JobPlayersLayout> JobPlayersLayouts = new(StringComparer.OrdinalIgnoreCase)
    {
        // "Player Data (Excel)" — STEPS layout, 40 columns.
        ["reporting_migrate.Get_JobPlayers_STEPS_Excel"] = new(
            "Player Data",
            [
                "RegistrationID", "FirstName", "LastName", "gender", "DOB",
                "Mom_FirstName", "Mom_LastName", "Mom_Cellphone", "Mom_Email",
                "Dad_FirstName", "Dad_LastName", "Dad_Cellphone", "Dad_Email",
                "agegroupName", "club_name", "teamName", "paid_total", "owed_total",
                "grad_year", "school_name", "school_grade", "position", "USLaxNo", "UsLaxNoExpiry",
                "jersey", "shorts", "kilt", "t-shirt", "reversible", "gloves", "uniform_no",
                "address", "city", "state", "zip", "Email", "Cellphone",
                "Requests", "medical_note", "shoes",
            ],
            r =>
            [
                r.RegistrationId, r.FirstName, r.LastName, r.Gender, r.Dob?.Date,
                r.MomFirstName, r.MomLastName, r.MomCellphone, r.MomEmail,
                r.DadFirstName, r.DadLastName, r.DadCellphone, r.DadEmail,
                r.AgegroupName, r.ClubName, r.TeamName, r.PaidTotal, r.OwedTotal,
                r.GradYear, r.SchoolName, r.SchoolGrade, r.Position, r.SportAssnId, r.SportAssnIdExpDate,
                r.JerseySize, r.ShortsSize, r.Kilt, r.TShirt, r.Reversible, r.Gloves, r.UniformNo,
                r.StreetAddress, r.City, r.State, r.PostalCode, r.Email, r.Cellphone,
                r.SpecialRequests, r.MedicalNote, r.Shoes,
            ]),

        // "Player Details (Excel)" — Yellow Jackets layout, 36 columns. Carries the raw
        // ClubTeamName, not the club-rep fallback.
        ["reporting_migrate.JobPlayers_YJ_Excel"] = new(
            "Player Details",
            [
                "RegistrationID", "uniform_no", "LastName", "FirstName", "address", "city", "state", "zip",
                "agegroupName", "teamName", "school_name", "grad_year", "position", "USLaxNo", "UsLaxNoExpiry",
                "jersey", "shorts", "t-shirt",
                "Mom_FirstName", "Mom_LastName", "Mom_Cellphone", "Mom_Email",
                "Dad_FirstName", "Dad_LastName", "Dad_Cellphone", "Dad_Email",
                "owed_total", "paid_total", "school_grade", "Requests", "gender", "RegDate", "DOB",
                "Cellphone", "Email", "ClubTeamName",
            ],
            r =>
            [
                r.RegistrationId, r.UniformNo, r.LastName, r.FirstName, r.StreetAddress, r.City, r.State, r.PostalCode,
                r.AgegroupName, r.TeamName, r.SchoolName, r.GradYear, r.Position, r.SportAssnId, r.SportAssnIdExpDate,
                r.JerseySize, r.ShortsSize, r.TShirt,
                r.MomFirstName, r.MomLastName, r.MomCellphone, r.MomEmail,
                r.DadFirstName, r.DadLastName, r.DadCellphone, r.DadEmail,
                r.OwedTotal, r.PaidTotal, r.SchoolGrade, r.SpecialRequests, r.Gender, r.RegistrationTs.Date, r.Dob?.Date,
                r.Cellphone, r.Email, r.ClubTeamName,
            ]),

        // "Player Data (Excel)" — E120 layout, 39 columns.
        ["reporting_migrate.Get_JobPlayers_E120_Excel"] = new(
            "Player Data",
            [
                "RegistrationID", "FirstName", "LastName", "Player", "gender", "DOB",
                "Mom", "Mom_Cellphone", "Mom_Email", "Dad", "Dad_Cellphone", "Dad_Email",
                "agegroupName", "teamName", "club_name", "paid_total", "owed_total",
                "grad_year", "school_name", "position", "USLaxNo", "UsLaxNoExpiry",
                "jersey", "shorts", "kilt", "t-shirt", "sweatshirt", "reversible", "gloves",
                "gpa", "sat", "act", "height_inches", "weight_lbs", "uniform_no",
                "address", "city", "state", "zip",
            ],
            r =>
            [
                r.RegistrationId, r.FirstName, r.LastName, SqlConcat(r.LastName, ", ", r.FirstName), r.Gender, r.Dob?.Date,
                SqlConcat(r.MomFirstName, " ", r.MomLastName), r.MomCellphone, r.MomEmail,
                SqlConcat(r.DadFirstName, " ", r.DadLastName), r.DadCellphone, r.DadEmail,
                r.AgegroupName, r.TeamName, r.ClubName, r.PaidTotal, r.OwedTotal,
                r.GradYear, r.SchoolName, r.Position, r.SportAssnId, r.SportAssnIdExpDate,
                r.JerseySize, r.ShortsSize, r.Kilt, r.TShirt, r.Sweatshirt, r.Reversible, r.Gloves,
                r.Gpa, r.Sat, r.Act, r.HeightInches, r.WeightLbs, r.UniformNo,
                r.StreetAddress, r.City, r.State, r.PostalCode,
            ]),

        // "Player Data (Excel)" — Liberty layout, 41 columns. No club column: Liberty jobs are club jobs.
        ["reporting_migrate.Get_JobPlayers_Liberty_Excel"] = new(
            "Player Data",
            [
                "RegistrationID", "Player", "gender", "DOB",
                "Mom", "Mom_Cellphone", "Mom_Email", "Dad", "Dad_Cellphone", "Dad_Email",
                "agegroupName", "teamName", "paid_total", "owed_total",
                "grad_year", "school_name", "position", "USLaxNo", "UsLaxNoExpiry",
                "jersey", "shorts", "kilt", "t-shirt", "sweatshirt", "reversible", "gloves",
                "gpa", "sat", "act", "height_inches", "weight_lbs", "uniform_no",
                "FirstName", "LastName", "address", "city", "state", "zip",
                "Email", "Cellphone", "Requests",
            ],
            r =>
            [
                r.RegistrationId, SqlConcat(r.LastName, ", ", r.FirstName), r.Gender, r.Dob?.Date,
                SqlConcat(r.MomFirstName, " ", r.MomLastName), r.MomCellphone, r.MomEmail,
                SqlConcat(r.DadFirstName, " ", r.DadLastName), r.DadCellphone, r.DadEmail,
                r.AgegroupName, r.TeamName, r.PaidTotal, r.OwedTotal,
                r.GradYear, r.SchoolName, r.Position, r.SportAssnId, r.SportAssnIdExpDate,
                r.JerseySize, r.ShortsSize, r.Kilt, r.TShirt, r.Sweatshirt, r.Reversible, r.Gloves,
                r.Gpa, r.Sat, r.Act, r.HeightInches, r.WeightLbs, r.UniformNo,
                r.FirstName, r.LastName, r.StreetAddress, r.City, r.State, r.PostalCode,
                r.Email, r.Cellphone, r.SpecialRequests,
            ]),

        // "Camp Data Dump (Excel)" — 17 columns.
        ["reporting_migrate.camp_datadump"] = new(
            "Camp Data Dump",
            [
                "FirstName", "LastName", "address", "city", "state", "zip",
                "Mom_Cellphone", "Mom_Email", "Dad_Cellphone", "Dad_Email", "Cellphone", "Email",
                "DOB", "grad_year", "school_name", "club_name", "position",
            ],
            r =>
            [
                r.FirstName, r.LastName, r.StreetAddress, r.City, r.State, r.PostalCode,
                r.MomCellphone, r.MomEmail, r.DadCellphone, r.DadEmail, r.Cellphone, r.Email,
                r.Dob?.Date, r.GradYear, r.SchoolName, r.ClubName, r.Position,
            ]),

        // "Camp Export (Long)(Excel)" — 20 columns.
        ["reporting_migrate.camp_excelexport_long"] = new(
            "Camp Export Long",
            [
                "RegistrationID", "teamName", "uniform_no", "LastName", "FirstName",
                "address", "city", "state", "zip",
                "Mom_Cellphone", "Mom_Email", "Dad_Cellphone", "Dad_Email", "Cellphone", "Email",
                "school_name", "club_name", "position", "grad_year", "roommate_pref",
            ],
            r =>
            [
                r.RegistrationId, r.TeamName, r.UniformNo, r.LastName, r.FirstName,
                r.StreetAddress, r.City, r.State, r.PostalCode,
                r.MomCellphone, r.MomEmail, r.DadCellphone, r.DadEmail, r.Cellphone, r.Email,
                r.SchoolName, r.ClubName, r.Position, r.GradYear, r.RoommatePref,
            ]),

        // "Camp Export (Short)(Excel)" — 6 columns.
        ["reporting_migrate.camp_excelexport_short"] = new(
            "Camp Export Short",
            ["teamName", "uniform_no", "LastName", "FirstName", "school_name", "club_name"],
            r => [r.TeamName, r.UniformNo, r.LastName, r.FirstName, r.SchoolName, r.ClubName]),

        // "Camper Export (Very Short)(Excel)" — 5 columns.
        ["reporting_migrate.camp_excelexport_veryshort"] = new(
            "Camper Very Short",
            ["teamName", "uniform_no", "LastName", "FirstName", "position"],
            r => [r.TeamName, r.UniformNo, r.LastName, r.FirstName, r.Position]),

        // "Day/Night Groups (Excel)" — 9 columns.
        ["reporting_migrate.camp_excelexport_daygroups"] = new(
            "Day Night Groups",
            [
                "agegroupName", "dayGroup", "nightGroup", "LastName", "FirstName",
                "roommate_pref", "school_name", "position", "uniform_no",
            ],
            r =>
            [
                r.AgegroupName, r.DayGroup, r.NightGroup, r.LastName, r.FirstName,
                r.RoommatePref, r.SchoolName, r.Position, r.UniformNo,
            ]),

        // "Roommates (Excel)" — 9 columns.
        ["reporting_migrate.camp_excelexport_roomies"] = new(
            "Roommates",
            [
                "teamName", "LastName", "FirstName", "DOB", "roommate_pref",
                "school_name", "school_grade", "club_name", "grad_year",
            ],
            r =>
            [
                r.TeamName, r.LastName, r.FirstName, r.Dob?.Date, r.RoommatePref,
                r.SchoolName, r.SchoolGrade, r.ClubName, r.GradYear,
            ]),

        // "Roommates (with position)(Excel)" — 11 columns. roomStatus is the proc's
        // left(teamName, 1): the team name's first character ('C' commuter, 'O' overnight).
        ["reporting_migrate.camp_excelexport_room_position"] = new(
            "Roommates w Position",
            [
                "teamName", "roomStatus", "LastName", "FirstName", "roommate_pref",
                "school_name", "school_grade", "club_name", "DOB", "position", "grad_year",
            ],
            r =>
            [
                r.TeamName, SqlLeft(r.TeamName, 1), r.LastName, r.FirstName, r.RoommatePref,
                r.SchoolName, r.SchoolGrade, r.ClubName, r.Dob?.Date, r.Position, r.GradYear,
            ]),

        // "Campers Export (Excel)" — 9 columns.
        ["reporting_migrate.camp_excelexport_summer"] = new(
            "Campers Summer",
            [
                "teamName", "LastName", "FirstName", "gender", "position", "school_grade",
                "RegDate", "paid_total", "owed_total",
            ],
            r =>
            [
                r.TeamName, r.LastName, r.FirstName, r.Gender, r.Position, r.SchoolGrade,
                r.RegistrationTs.Date, r.PaidTotal, r.OwedTotal,
            ]),
    };

    /// <summary>
    /// T-SQL <c>a + sep + b</c>: null when either side is null, where C# concatenation would
    /// treat null as empty.
    /// </summary>
    private static string? SqlConcat(string? left, string separator, string? right) =>
        left == null || right == null ? null : left + separator + right;

    /// <summary>T-SQL <c>left(value, length)</c>: null stays null, a short value comes back whole.</summary>
    private static string? SqlLeft(string? value, int length) =>
        value == null ? null : value.Length <= length ? value : value[..length];

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
        return new StackArtifacts(ExcelWorkbookWriter.Build(sheets), iifBytes, sourceTrns, consolidatedTrns);
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

        return ExcelWorkbookWriter.Build(sheets);
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
        => ExcelWorkbookWriter.Build(await ReadReaderIntoSheetsAsync(reader));

    /// <summary>
    /// One in-memory result set from a multi-sheet stored procedure: the sheet name (from the
    /// "QA Test:" marker or the legacy default), the column names, and the raw row values.
    /// Both the Excel renderer and the QuickBooks IIF renderer project from this single model,
    /// so a reconciliation SP is executed exactly once yet yields two consistent artifacts.
    /// </summary>
    // SheetData is an alias for the shared ExcelSheet — see the using at the top of the file.

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
                        // Everything after the marker prefix — Split(':')[1] would silently
                        // truncate a name that itself contains a colon.
                        var sheetName = value["QA Test:".Length..].Trim();
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
