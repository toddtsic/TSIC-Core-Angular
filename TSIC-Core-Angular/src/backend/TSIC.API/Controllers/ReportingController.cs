using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Reporting;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos;
using TSIC.Domain.Constants;

namespace TSIC.API.Controllers;

/// <summary>
/// Reporting controller — proxies Crystal Reports exports, stored procedure Excel exports,
/// and iCal schedule exports. All context (jobId, regId, userId) is derived from JWT claims.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ReportingController : ControllerBase
{
    private readonly IReportingService _reportingService;
    private readonly IJobLookupService _jobLookupService;
    private readonly IDailyRegCountsPdfService _dailyRegCountsService;
    private readonly IInvoiceReportPdfService _invoiceReportService;
    private readonly IFeeYtdReportPdfService _feeYtdReportService;
    private readonly IPlayerStatsReportPdfService _playerStatsReportService;
    private readonly IAmericanSelectReportPdfService _americanSelectReportService;
    private readonly IPackedRosterPdfService _packedRosterService;
    private readonly IGameBoardsPdfService _gameBoardsPdfService;
    private readonly IRosterTablePdfService _rosterTableService;
    private readonly IShowcaseScheduleReportService _showcaseScheduleService;

    // JWT carries the role NAME ("Director"); reporting.JobReports.RoleId is the role-id GUID.
    // Mirrors the local map pattern used by NavController / WidgetDashboardService /
    // UserWidgetService / AdministratorService — no shared abstraction yet.
    private static readonly Dictionary<string, string> RoleNameToIdMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Director"] = RoleConstants.Director,
        ["SuperDirector"] = RoleConstants.SuperDirector,
        ["Superuser"] = RoleConstants.Superuser,
        ["Family"] = RoleConstants.Family,
        ["Player"] = RoleConstants.Player,
        ["Club Rep"] = RoleConstants.ClubRep,
        ["Ref Assignor"] = RoleConstants.RefAssignor,
        ["Staff"] = RoleConstants.Staff,
        ["Store Admin"] = RoleConstants.StoreAdmin,
        ["STPAdmin"] = RoleConstants.StpAdmin,
    };

    public ReportingController(
        IReportingService reportingService,
        IJobLookupService jobLookupService,
        IDailyRegCountsPdfService dailyRegCountsService,
        IInvoiceReportPdfService invoiceReportService,
        IFeeYtdReportPdfService feeYtdReportService,
        IPlayerStatsReportPdfService playerStatsReportService,
        IAmericanSelectReportPdfService americanSelectReportService,
        IPackedRosterPdfService packedRosterService,
        IGameBoardsPdfService gameBoardsPdfService,
        IRosterTablePdfService rosterTableService,
        IShowcaseScheduleReportService showcaseScheduleService)
    {
        _reportingService = reportingService;
        _jobLookupService = jobLookupService;
        _dailyRegCountsService = dailyRegCountsService;
        _invoiceReportService = invoiceReportService;
        _feeYtdReportService = feeYtdReportService;
        _playerStatsReportService = playerStatsReportService;
        _americanSelectReportService = americanSelectReportService;
        _packedRosterService = packedRosterService;
        _gameBoardsPdfService = gameBoardsPdfService;
        _rosterTableService = rosterTableService;
        _showcaseScheduleService = showcaseScheduleService;
    }

    /// <summary>
    /// Translates the caller's role-name claims into role-id GUIDs (the form
    /// stored in <c>reporting.JobReports.RoleId</c>). Unknown role names are dropped.
    /// </summary>
    private string[] GetCallerRoleIds()
        => User.FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .Select(name => RoleNameToIdMap.TryGetValue(name, out var id) ? id : null)
            .Where(id => id != null)
            .Cast<string>()
            .ToArray();

    // ──────────────────────────────────────────────────────────────
    // Helpers — derive all context from JWT claims, never from params
    // ──────────────────────────────────────────────────────────────

    private async Task<ActionResult> CrystalReportAsync(string reportName, int exportFormat, string? strGids = null)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        var regId = User.GetRegistrationId();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // For anonymous requests, jobId may be null — CR service handles Guid.Empty
        var result = await _reportingService.ExportCrystalReportAsync(
            reportName, exportFormat, jobId ?? Guid.Empty, regId, userId, strGids);

        // Record export history for authenticated users
        await _reportingService.RecordExportHistoryAsync(regId, null, reportName);

        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    // ──────────────────────────────────────────────────────────────
    // Reports library — sourced from reporting.JobReports
    // ──────────────────────────────────────────────────────────────

    [HttpGet("catalogue")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<List<JobReportEntryDto>>> GetCatalogue(CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return new List<JobReportEntryDto>();
        }

        // SuperUser sees every role's reports (each tagged with RoleName) so role
        // assignment is visible in the library; everyone else gets exactly the rows
        // their own roles entitle them to. Row existence IS the entitlement.
        var rows = User.IsInRole("Superuser")
            ? await _reportingService.GetAllJobReportsAsync(jobId.Value, cancellationToken)
            : await _reportingService.GetJobReportsAsync(jobId.Value, GetCallerRoleIds(), cancellationToken);
        return rows;
    }

    // ──────────────────────────────────────────────────────────────
    // SuperUser library editor (per-Job, per-Role)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Roles with at least one row in reporting.JobReports for the current job.
    /// Drives the editor's role-picker dropdown.
    /// </summary>
    [HttpGet("editor/job-roles")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<ActionResult<List<JobReportEditorRoleDto>>> GetEditorRoles(CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest("Job ID could not be determined from user token");

        var rows = await _reportingService.GetEditorRolesAsync(jobId.Value, cancellationToken);
        return rows;
    }

    /// <summary>
    /// All rows in reporting.JobReports for the current job + given roleId.
    /// </summary>
    [HttpGet("editor")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<ActionResult<List<JobReportEditorRowDto>>> GetEditorRows(
        [FromQuery] string roleId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(roleId)) return BadRequest("roleId is required");

        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest("Job ID could not be determined from user token");

        var rows = await _reportingService.GetEditorRowsAsync(jobId.Value, roleId, cancellationToken);
        return rows;
    }

    /// <summary>
    /// Updates an editor row. Service validates that the row's JobId matches the
    /// caller's current job (from JWT) before mutating.
    /// </summary>
    [HttpPut("editor/{jobReportId:guid}")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<ActionResult<JobReportEditorRowDto>> UpdateEditorRow(
        Guid jobReportId,
        [FromBody] JobReportEditorUpdateDto dto,
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest("Job ID could not be determined from user token");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized("User ID not found in token");

        var updated = await _reportingService.UpdateEditorRowAsync(
            jobReportId, jobId.Value, dto, userId, cancellationToken);
        if (updated == null) return NotFound();
        return updated;
    }

    /// <summary>
    /// Creates a new editor row for the caller's current job. JobId is derived from
    /// JWT (never trusted from client); RoleId comes from the editor's role-picker.
    /// Returns 409 Conflict if (JobId, RoleId, Controller, Action, GroupLabel) collides
    /// with an existing row.
    /// </summary>
    [HttpPost("editor")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<ActionResult<JobReportEditorRowDto>> CreateEditorRow(
        [FromBody] JobReportEditorCreateDto dto,
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest("Job ID could not be determined from user token");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized("User ID not found in token");

        var (row, conflict) = await _reportingService.CreateEditorRowAsync(
            jobId.Value, dto, userId, cancellationToken);

        if (conflict)
        {
            return Conflict(new { message = "A report with that Controller, Action, and Group already exists for this role." });
        }
        return row!;
    }

    // ──────────────────────────────────────────────────────────────
    // Stored Procedure Excel Exports
    // ──────────────────────────────────────────────────────────────

    [HttpGet("export-sp")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult> ExportStoredProcedureResults(
        [FromQuery] string spName,
        [FromQuery] bool bUseJobId,
        [FromQuery] bool bUseDateUnscheduled = false,
        CancellationToken cancellationToken = default)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService) ?? Guid.Empty;

        // Per-row entitlement check on top of the [Authorize(AdminOnly)] floor — confirms
        // the caller has an active stored-proc row in reporting.JobReports for this spName.
        // Prevents an Admin from running a proc they aren't entitled to via direct URL.
        // SuperUser can run any report visible in its all-roles catalogue, so its check
        // isn't scoped to the SU role — but the SP must still be a real configured report
        // for the job. Everyone else is checked against their own roles.
        var entitled = User.IsInRole("Superuser")
            ? await _reportingService.HasStoredProcedureEntitlementAnyRoleAsync(
                jobId, spName, cancellationToken)
            : await _reportingService.HasStoredProcedureEntitlementAsync(
                jobId, GetCallerRoleIds(), spName, cancellationToken);
        if (!entitled)
        {
            return Forbid();
        }

        var regId = User.GetRegistrationId();

        var result = await _reportingService.ExportStoredProcedureToExcelAsync(
            spName, jobId, bUseJobId, bUseDateUnscheduled);

        await _reportingService.RecordExportHistoryAsync(regId, spName, null);

        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    // ──────────────────────────────────────────────────────────────
    // Bold Reports (RDL → PDF) Exports
    // ──────────────────────────────────────────────────────────────

    [HttpGet("export-bold")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult> ExportBoldReport(
        [FromQuery] string reportName,
        CancellationToken cancellationToken = default)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService) ?? Guid.Empty;

        // Per-row entitlement check on top of [Authorize(AdminOnly)] — same shape as
        // the export-sp endpoint, just keyed on the BoldReport row's reportName param.
        var entitled = User.IsInRole("Superuser")
            ? await _reportingService.HasBoldReportEntitlementAnyRoleAsync(
                jobId, reportName, cancellationToken)
            : await _reportingService.HasBoldReportEntitlementAsync(
                jobId, GetCallerRoleIds(), reportName, cancellationToken);
        if (!entitled)
        {
            return Forbid();
        }

        var regId = User.GetRegistrationId();

        var result = await _reportingService.ExportBoldReportAsync(
            reportName, jobId, cancellationToken);

        await _reportingService.RecordExportHistoryAsync(regId, null, reportName);

        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    [HttpGet("export-monthly-reconciliation")]
    [Authorize(Roles = "Superuser")]
    public async Task<ActionResult> ExportMonthlyReconciliationDataToExcel(
        [FromQuery] int settlementMonth,
        [FromQuery] int settlementYear)
    {
        var result = await _reportingService.ExportMonthlyReconciliationAsync(
            settlementMonth, settlementYear, isMerchandise: false);

        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    [HttpGet("export-monthly-reconciliation-merch")]
    [Authorize(Roles = "Superuser")]
    public async Task<ActionResult> ExportMonthlyReconciliationDataToExcelMerch(
        [FromQuery] int settlementMonth,
        [FromQuery] int settlementYear)
    {
        var result = await _reportingService.ExportMonthlyReconciliationAsync(
            settlementMonth, settlementYear, isMerchandise: true);

        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    // ──────────────────────────────────────────────────────────────
    // iCal Export
    // ──────────────────────────────────────────────────────────────

    [HttpPost("schedule-ical")]
    [AllowAnonymous]
    public async Task<ActionResult> ScheduleExportIcal([FromBody] ScheduleICalExportRequest model)
    {
        var gameIds = JsonSerializer.Deserialize<List<int>>(model.StrListGidsIcal) ?? new List<int>();
        var result = await _reportingService.ExportScheduleToICalAsync(gameIds);
        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    // ──────────────────────────────────────────────────────────────
    // Crystal Reports — No Auth / Public
    // ──────────────────────────────────────────────────────────────

    // Daily registration counts — EF + Syncfusion replacement for the legacy Crystal
    // "JobPlayers_TSICDaily" (proc reporting.Get_Registrations_TSIC_Today). Cross-job, public.
    [HttpGet("Get_JobPlayers_TSICDAILY")]
    public async Task<ActionResult> GetJobPlayersTsicDaily(CancellationToken cancellationToken)
    {
        var result = await _dailyRegCountsService.GenerateAsync(cancellationToken);
        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    [HttpGet("Score_Input")]
    public Task<ActionResult> ScoreInput()
        => CrystalReportAsync("Score_Input", 1);

    [HttpGet("Job_Rosters_NoMedical")]
    public Task<ActionResult> JobRostersNoMedical([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("Job_Rosters_NoMedical", exportFormat);

    [HttpGet("Club_AllJobs_Rosters_NoMedical")]
    public Task<ActionResult> ClubAllJobsRostersNoMedical([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("Club_AllJobs_Rosters_NoMedical", exportFormat);

    [HttpGet("Job_Club_Rosters")]
    public Task<ActionResult> JobClubRosters([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("Job_Club_Rosters", exportFormat);

    [HttpGet("JobRosters_TryoutsCheckReport")]
    public Task<ActionResult> JobRostersTryoutsCheckReport([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("JobRosters_TryoutsCheckReport", exportFormat);

    [HttpGet("League_StandingsExcel")]
    public Task<ActionResult> LeagueStandingsExcel([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("League_StandingsExcel", exportFormat);

    // ──────────────────────────────────────────────────────────────
    // Crystal Reports — AllowAnonymous
    // ──────────────────────────────────────────────────────────────

    [HttpGet("Schedule_Export")]
    [AllowAnonymous]
    public Task<ActionResult> ScheduleExport([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("Schedule_Export", exportFormat);

    [HttpPost("Schedule_Export_Public")]
    [AllowAnonymous]
    public Task<ActionResult> ScheduleExportPublic([FromBody] ScheduleExportRequest model)
        => CrystalReportAsync("Schedule_Export_Public", int.Parse(model.ExportFormat), model.StrListGids);

    [HttpGet("FieldUtilizationAcrossLeaguesByDate")]
    [AllowAnonymous]
    public Task<ActionResult> FieldUtilizationAcrossLeaguesByDate([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("FieldUtilizationAcrossLeaguesByDate", exportFormat);

    [HttpGet("TournyCheckin")]
    [AllowAnonymous]
    public Task<ActionResult> TournyCheckin([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("tournycheckin", exportFormat);

    [HttpGet("CovidTournyCheckin")]
    [AllowAnonymous]
    public Task<ActionResult> CovidTournyCheckin([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("covidtournycheckin", exportFormat);

    [HttpGet("AmericanSelectTournyCheckin")]
    [AllowAnonymous]
    public Task<ActionResult> AmericanSelectTournyCheckin([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("americanselecttournycheckin", exportFormat);

    // American Select main-event rosters — the offer-team rosters are just a packed roster, so
    // they're served by the shared PackedRoster engine with a fixed AS preset (Player/Position/
    // School, sorted by position, 2-up) instead of a bespoke renderer. requiresSchedule:false
    // includes the offer teams (which play no scheduled games; the schedule gate would exclude
    // them) and the engine's job/agegroup scope drops the "Registration" (tryout) teams. Job from JWT.
    [HttpGet("AmericanSelectMainEventRosters")]
    [AllowAnonymous]
    public async Task<ActionResult> AmericanSelectMainEventRosters(CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        var request = new PackedRosterRequestDto
        {
            NUp = 2,
            Columns = new[]
            {
                new PackedRosterColumnDto { Key = "player", WidthWeight = 80, Align = "Left", LongText = "" },
                new PackedRosterColumnDto { Key = "position", WidthWeight = 38, Align = "Left", LongText = "" },
                new PackedRosterColumnDto { Key = "school_name", WidthWeight = 90, Align = "Left", LongText = "Wrap" },
            },
            ShowCoaches = false,
            ShowRepName = false,
            ShowRepEmail = false,
            ShowRepPhone = false,
            SchoolShowsCommit = false,
            ShowClubAffiliation = false,
            SortBy = "Position",
            RequiresSchedule = false,
        };
        var result = await _packedRosterService.GenerateAsync(request, jobId ?? Guid.Empty, cancellationToken);
        return File(result.FileBytes, result.ContentType, "AmericanSelectMainEventRosters.pdf");
    }

    // American Select tryout evaluation — EF + Syncfusion replacement for Crystal
    // "americanselectevaluation" (proc reporting.AmericanSelectPlayerData). Job from JWT.
    [HttpGet("AmericanSelectEvaluation")]
    [AllowAnonymous]
    public async Task<ActionResult> AmericanSelectEvaluation(CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        var result = await _americanSelectReportService.GenerateEvaluationAsync(jobId ?? Guid.Empty, cancellationToken);
        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    [HttpGet("FieldUtilizationAcrossLeaguesByDateTournament")]
    [AllowAnonymous]
    public Task<ActionResult> FieldUtilizationAcrossLeaguesByDateTournament([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("FieldUtilizationAcrossLeaguesByDateTournament", exportFormat);

    [HttpGet("FieldUtilizationAcrossLeaguesTournament")]
    [AllowAnonymous]
    public Task<ActionResult> FieldUtilizationAcrossLeaguesTournament([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("FieldUtilizationAcrossLeaguesTournament", exportFormat);

    [HttpGet("TournamentRosterPacked")]
    [AllowAnonymous]
    public Task<ActionResult> TournamentRosterPacked([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("TournamentRosterPacked", exportFormat);

    [HttpGet("TournamentRosterPacked_PositionSchool_Public")]
    [AllowAnonymous]
    public Task<ActionResult> TournamentRosterPackedPositionSchoolPublic([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("TournamentRosterPacked_PositionSchool_Public", exportFormat);

    [HttpGet("Job_ClubRep_And_Coaches")]
    [AllowAnonymous]
    public Task<ActionResult> JobClubRepAndCoaches([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("Job_ClubRep_And_Coaches", exportFormat);

    [HttpGet("Get_JobRosters_RecruitingReport_Public_DumpExcel")]
    [AllowAnonymous]
    public Task<ActionResult> GetJobRostersRecruitingReportPublicDumpExcel([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("JobRosters_RecruitingReport_Public_DumpExcel", exportFormat);

    [HttpGet("Get_JobRosters_PackedByPositionAGNoClubPlayers")]
    [AllowAnonymous]
    public Task<ActionResult> GetJobRostersPackedByPositionAgNoClubPlayers([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("JobRosters_PackedByPositionAGNoClub", exportFormat);

    [HttpGet("Get_JobRosters_PackedByPosition_XPO")]
    [AllowAnonymous]
    public Task<ActionResult> GetJobRostersPackedByPositionXpo([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("JobRosters_PackedByPosition_XPO", exportFormat);

    [HttpGet("Schedule_ExportExcel")]
    [AllowAnonymous]
    public Task<ActionResult> ScheduleExportExcel([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("Schedule_ExportExcel", exportFormat);

    // Game Cards — EF + Syncfusion replacement for Crystal "Schedule_Gamecards": 2-up blank
    // score cards grouped by field. Job from JWT.
    [HttpGet("Schedule_Gamecards")]
    [AllowAnonymous]
    public async Task<ActionResult> ScheduleGamecards(CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        var result = await _showcaseScheduleService.GenerateGameCardsAsync(jobId ?? Guid.Empty, cancellationToken);
        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    [HttpGet("Schedule_ExportExcel_Unscored")]
    [AllowAnonymous]
    public Task<ActionResult> ScheduleExportExcelUnscored([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("Schedule_ExportExcel_Unscored", exportFormat);

    // ──────────────────────────────────────────────────────────────
    // Crystal Reports — Superuser Role
    // ──────────────────────────────────────────────────────────────

    [HttpGet("Produce_Job_Invoices_LastMonth")]
    [Authorize(Roles = "Superuser")]
    public async Task<ActionResult<bool>> ProduceJobInvoicesLastMonth()
    {
        var result = await _reportingService.BuildLastMonthsJobInvoicesAsync();
        return Ok(result);
    }

    // Monthly client invoices — EF + Syncfusion replacement for Crystal "invoices2015"
    // (proc adn.rpt_invoice). Renders the most recently completed month across all jobs.
    [HttpGet("Get_Invoices_LastMonth")]
    [Authorize(Roles = "Superuser")]
    public async Task<ActionResult> GetInvoicesLastMonth(CancellationToken cancellationToken)
    {
        var result = await _invoiceReportService.GenerateItemizedAsync(cancellationToken);
        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    [HttpGet("Get_Invoices_LastMonthSummariesOnly")]
    [Authorize(Roles = "Superuser")]
    public async Task<ActionResult> GetInvoicesLastMonthSummariesOnly(CancellationToken cancellationToken)
    {
        var result = await _invoiceReportService.GenerateSummaryOnlyAsync(cancellationToken);
        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    // ──────────────────────────────────────────────────────────────
    // Crystal Reports — Superuser/Director/Player Roles
    // ──────────────────────────────────────────────────────────────

    [HttpGet("Get_OutdoorEdRosters")]
    [Authorize(Roles = "Superuser,Director,Player")]
    public Task<ActionResult> GetOutdoorEdRosters([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("outdooredrosters", exportFormat);

    [HttpGet("Get_OutdoorEdRostersSpringOnly")]
    [Authorize(Roles = "Superuser,Director,Player")]
    public Task<ActionResult> GetOutdoorEdRostersSpringOnly([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("outdooredrostersspringonly", exportFormat);

    // ──────────────────────────────────────────────────────────────
    // Crystal Reports — Club Rep Role
    // ──────────────────────────────────────────────────────────────

    [HttpGet("ClubRep_BalanceDue_ByAgegroupTeamFee")]
    [Authorize(Roles = "Club Rep")]
    public Task<ActionResult> ClubRepBalanceDueByAgegroupTeamFee([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("ClubRep_BalanceDue_ByAgegroupTeamFee", exportFormat);

    // ──────────────────────────────────────────────────────────────
    // Crystal Reports — StoreAdmin Policy
    // ──────────────────────────────────────────────────────────────

    [HttpGet("StoreLabels")]
    [Authorize(Policy = "StoreAdmin")]
    public Task<ActionResult> StoreLabels([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("StoreLabels3", exportFormat);

    [HttpGet("StorePickupSignoff")]
    [Authorize(Policy = "StoreAdmin")]
    public Task<ActionResult> StorePickupSignoff([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("StorePickupSignoff", exportFormat);

    [HttpGet("StorePerPlayerPickup")]
    [Authorize(Policy = "StoreAdmin")]
    public Task<ActionResult> StorePerPlayerPickup([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("StorePerPlayerPickup", exportFormat);

    [HttpGet("StorePerPlayerPivot")]
    [Authorize(Policy = "StoreAdmin")]
    public Task<ActionResult> StorePerPlayerPivot([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("StorePerPlayerPivot", exportFormat);

    // ──────────────────────────────────────────────────────────────
    // Crystal Reports — AdminOnly Policy
    // ──────────────────────────────────────────────────────────────

    // TSIC fee YTD comparison — EF + Syncfusion replacement for Crystal "tsicTSICFeesYTD"
    // (proc adn.tsicFeesYTDAndLastYear). This-year-YTD vs last-year-YTD across all jobs, by
    // customer + job. PDF only (exportFormat is legacy and ignored).
    [HttpGet("TSICFeesYTDByCustomerAndJob")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult> TsicFeesYtdByCustomerAndJob(CancellationToken cancellationToken)
    {
        var result = await _feeYtdReportService.GenerateByCustomerAndJobAsync(cancellationToken);
        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    // Customer rollup variant — Crystal "tsicTSICFeesYTDByCustomer" (same proc, no job breakout).
    [HttpGet("TSICFeesYTDByCustomer")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult> TsicFeesYtdByCustomer(CancellationToken cancellationToken)
    {
        var result = await _feeYtdReportService.GenerateByCustomerAsync(cancellationToken);
        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    [HttpGet("ISP_CheckinFlat")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> IspCheckinFlat()
        => CrystalReportAsync("ISPCheckinFlat", 1);

    [HttpGet("Get_JobPlayers_STEPS")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> GetJobPlayersSteps([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("JobPlayers_STEPS", exportFormat);

    [HttpGet("Job_CampCheckin")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> JobCampCheckin()
        => CrystalReportAsync("Job_CampCheckin", 1);

    [HttpGet("Job_CampCheckinII")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> JobCampCheckinII()
        => CrystalReportAsync("Job_CampCheckinII", 1);

    // E120 player-stats entry form — EF + Syncfusion replacement for Crystal "PlayerStats_E120"
    // (proc reporting.PlayerStats_E120). Job-scoped from JWT.
    [HttpGet("PlayerStats_E120")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult> PlayerStatsE120(CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        var result = await _playerStatsReportService.GenerateE120Async(jobId ?? Guid.Empty, cancellationToken);
        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    [HttpGet("CustomerJobRevenueRollups")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> CustomerJobRevenueRollups()
        => CrystalReportAsync("CustomerJobRevenueRollups", 1);

    [HttpGet("Get_CustomerPlayers1")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> GetCustomerPlayers1([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("CustomerPlayers1", exportFormat);

    [HttpGet("Get_DiscountedPlayers")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> GetDiscountedPlayers([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("DiscountedPlayers", exportFormat);

    [HttpGet("Get_TeamFieldDistribution")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> GetTeamFieldDistribution([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("teamfielddistribution", exportFormat);

    [HttpGet("clubrostersNoMedicalII")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> ClubrostersNoMedicalII([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("clubrostersNoMedicalII", exportFormat);

    [HttpGet("LeagueForfeitReport")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> LeagueForfeitReport([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("LeagueForfeitReport", exportFormat);

    [HttpGet("LeagueRefReport")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> LeagueRefReport([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("LeagueRefReport", exportFormat);

    [HttpGet("League_Teams")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> LeagueTeams([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("League_Teams", exportFormat);

    [HttpGet("League_Coaches")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> LeagueCoaches([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("League_Coaches", exportFormat);

    [HttpGet("League_Standings")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> LeagueStandings([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("League_Standings", exportFormat);

    [HttpGet("Mobile_JobUsers")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> MobileJobUsers([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("Mobile_JobUsers", exportFormat);

    [HttpGet("League_ClubReps")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> LeagueClubReps([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("League_ClubReps", exportFormat);

    [HttpGet("ScheduleMaster")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> ScheduleMaster([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("ScheduleMaster", exportFormat);

    [HttpGet("ScheduleByAgT")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> ScheduleByAgT([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("ScheduleByAgT", exportFormat);

    // Schedules by Age Group and Team — EF + Syncfusion replacement for Crystal
    // "ScheduleByClubAgTPerPage": one page per team listing that team's games (each game prints
    // on both teams' pages). Job from JWT.
    [HttpGet("ScheduleByClubAgTPerPage")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult> ScheduleByClubAgTPerPage(CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        var result = await _showcaseScheduleService.GenerateScheduleByTeamAsync(jobId ?? Guid.Empty, cancellationToken);
        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    [HttpGet("ScheduleByClubAgT")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> ScheduleByClubAgT([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("ScheduleByClubAgT", exportFormat);

    [HttpGet("ScheduleByAgDiv")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> ScheduleByAgDiv([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("ScheduleByAgDiv", exportFormat);

    [HttpGet("tournamentumml2")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> Tournamentumml2([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("tournamentumml2", exportFormat);

    [HttpGet("tournamentumml1")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> Tournamentumml1([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("tournamentumml1", exportFormat);

    [HttpGet("Get_Team_Transactions_ForExcelExport")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> GetTeamTransactionsForExcelExport([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("JobTeamTransactions", exportFormat);

    // Field Utilization with Player Nominations — EF + Syncfusion replacement for Crystal
    // "FieldUtilizationWithNominations": games grouped by date+field, boxed score + a blank
    // Player Nominations write-in grid per game. Job from JWT.
    [HttpGet("FieldUtilizationWithNominations")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult> FieldUtilizationWithNominations(CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        var result = await _showcaseScheduleService.GenerateFieldUtilizationNominationsAsync(jobId ?? Guid.Empty, cancellationToken);
        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    [HttpGet("TournamentRosterPacked_PositionSchool")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> TournamentRosterPackedPositionSchool([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("TournamentRosterPacked_PositionSchool", exportFormat);

    [HttpGet("TournamentRecruitingReport")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> TournamentRecruitingReport([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("TournamentRecruitingReport", exportFormat);

    // American Select recruiting CONTACT sheet — EF + Syncfusion replacement for Crystal
    // "TournamentRecruitingReportASL". Off the shared roster query (showcase scope), grouped
    // by agegroup with staff contact cards then player cards. Job from JWT.
    [HttpGet("TournamentRecruitingReportASL")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult> TournamentRecruitingReportAsl(CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        var result = await _packedRosterService.GenerateRecruiterAslAsync(jobId ?? Guid.Empty, cancellationToken);
        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    // American Select recruiting STAT-CAPTURE sheet — EF + Syncfusion replacement for Crystal
    // "TournamentRecruitingReportUSL". Same roster data as ASL; blank hand-entry G/A/GB/DC/S
    // grid (stats are recorded by hand, not stored). Job from JWT.
    [HttpGet("TournamentRecruitingReportUSL")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult> TournamentRecruitingReportUsl(CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        var result = await _packedRosterService.GenerateRecruiterUslAsync(jobId ?? Guid.Empty, cancellationToken);
        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    [HttpGet("TournamentPlayers_RosterRequestAndRegistrants_DataDump")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> TournamentPlayersRosterRequestDataDump([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("TournamentPlayers_RosterRequestAndRegistrants_DataDump", exportFormat);

    [HttpGet("TournamentRecruitingReport_DataDump")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> TournamentRecruitingReportDataDump([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("TournamentRecruitingReport_DataDump", exportFormat);

    [HttpGet("Get_JobPlayer_Transactions")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> GetJobPlayerTransactions([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("JobPlayerTransactions", exportFormat);

    [HttpGet("Get_ClubAllJobPlayerTransactions")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> GetClubAllJobPlayerTransactions([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("ClubAllJobPlayerTransactions", exportFormat);

    [HttpGet("JobTeams_WithClubRep_AllTransactions_Excel")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> JobTeamsWithClubRepAllTransactionsExcel([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("JobTeams_WithClubRep_AllTransactions_Excel", exportFormat);

    [HttpGet("JobTeams_WithClubRep_Excel")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> JobTeamsWithClubRepExcel([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("JobTeams_WithClubRep_Excel", exportFormat);

    [HttpGet("Rosters_WithClubRep_A")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> RostersWithClubRepA()
        => CrystalReportAsync("Rosters_WithClubRep_A", 1);

    [HttpGet("Get_JobPlayers_Liberty_Excel")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> GetJobPlayersLibertyExcel()
        => CrystalReportAsync("JobPlayers_Liberty", 3);

    [HttpGet("Get_JobPlayers_STEPS_Excel")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> GetJobPlayersStepsExcel()
        => CrystalReportAsync("JobPlayers_STEPS_Excel", 3);

    [HttpGet("Get_JobPlayers_E120_Excel")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> GetJobPlayersE120Excel()
        => CrystalReportAsync("JobPlayers_E120_Excel", 3);

    [HttpGet("JobStaff_Excel")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> JobStaffExcel()
        => CrystalReportAsync("JobStaff_Excel", 3);

    // Game Boards — EF + Syncfusion replacement for Crystal "Schedule_ByAgegroup" (master-detail proc
    // pair Schedule_Get_AgegroupScorecard + Schedule_Get_DivTeamsAndStandings, flattened). A blank
    // game-day scoring board grouped agegroup → division (standings box + games) + a per-agegroup
    // championship round. Job from JWT.
    [HttpGet("Schedule_ByAgegroup")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult> ScheduleByAgegroup(CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        var result = await _gameBoardsPdfService.GenerateAsync(jobId ?? Guid.Empty, cancellationToken);
        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    [HttpGet("JobRosters_MSYSA")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> JobRostersMsysa([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("JobRosters_MSYSA", exportFormat);

    [HttpGet("Get_JobRosters_RecruitingReport")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> GetJobRostersRecruitingReport([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("JobRosters_RecruitingReport", exportFormat);

    [HttpGet("Get_JobRosters_RecruitingReport_XPO")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> GetJobRostersRecruitingReportXpo([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("JobRosters_RecruitingReport_XPO", exportFormat);

    [HttpGet("JobRosters_DayGroupsPackedXPO")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> JobRostersDayGroupsPackedXpo([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("JobRosters_DayGroupsPackedXPO", exportFormat);

    [HttpGet("PlayerStats_ParisiExportExcel")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> PlayerStatsParisiExportExcel([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("PlayerStats_ParisiExportExcel", exportFormat);

    [HttpGet("JobPlayers_YJ_Excel")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> JobPlayersYjExcel([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("JobPlayers_YJ_Excel", exportFormat);

    [HttpGet("Club_JobPlayers_Excel")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> ClubJobPlayersExcel([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("Club_JobPlayers_Excel", exportFormat);

    [HttpGet("JobPlayers_Showcase_Excel")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> JobPlayersShowcaseExcel([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("JobPlayers_Showcase_Excel", exportFormat);

    [HttpGet("Get_JobRosters_RecruitingReport_DumpExcel")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> GetJobRostersRecruitingReportDumpExcel([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("JobRosters_RecruitingReport_DumpExcel", exportFormat);

    [HttpGet("Job_UniformData")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> JobUniformData([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("UniformData", exportFormat);

    // Camp reports

    [HttpGet("camp_commuters")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> CampCommuters([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("camp_commuters", exportFormat);

    [HttpGet("CustomerJobPlayerRollup")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> CustomerJobPlayerRollup([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("CustomerJobPlayerRollup", exportFormat);

    [HttpGet("camp_daygroups")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> CampDaygroups([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("camp_daygroups", exportFormat);

    [HttpGet("camp_nightgroups_pdf")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> CampNightgroupsPdf([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("camp_nightgroups_pdf", exportFormat);

    [HttpGet("camp_daygroups_pdf")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> CampDaygroupsPdf([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("camp_daygroups_pdf", exportFormat);

    [HttpGet("camp_nightgroups")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> CampNightgroups([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("camp_nightgroups", exportFormat);

    [HttpGet("camp_roomies")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> CampRoomies([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("camp_roomies", exportFormat);

    [HttpGet("camp_datadump")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> CampDatadump([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("camp_datadump", exportFormat);

    [HttpGet("camp_excelexport_summer")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> CampExcelexportSummer([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("camp_excelexport_summer", exportFormat);

    // Camp summer roster (PDF) — EF + Syncfusion replacement for Crystal
    // "camp_excelexport_summer_pdf", served by the shared Roster Table engine with a fixed
    // camp preset: grouped by session (agegroup), Player / Phone / Allergies (= medical note) /
    // Mom (name+phone) / Dad (name+phone). Job from JWT.
    [HttpGet("camp_excelexport_summer_pdf")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult> CampExcelexportSummerPdf(CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        var request = new RosterTableRequestDto
        {
            GroupBy = "AgeGroup",
            SortBy = "Name",
            Orientation = "Portrait",
            PlayersOnly = true,
            PageBreakPerGroup = true,
            ColorAccent = false,
            Columns = new[]
            {
                new RosterTableColumnDto { Key = "player", WidthWeight = 95, Align = "Left", LongText = "Wrap" },
                new RosterTableColumnDto { Key = "phone", WidthWeight = 60, Align = "Left", LongText = "Truncate" },
                new RosterTableColumnDto { Key = "allergies", WidthWeight = 70, Align = "Left", LongText = "Wrap" },
                new RosterTableColumnDto { Key = "momContact", WidthWeight = 110, Align = "Left", LongText = "Wrap" },
                new RosterTableColumnDto { Key = "dadContact", WidthWeight = 110, Align = "Left", LongText = "Wrap" },
            },
        };
        var result = await _rosterTableService.GenerateAsync(request, jobId ?? Guid.Empty, cancellationToken);
        return File(result.FileBytes, result.ContentType, "camp_excelexport_summer.pdf");
    }

    [HttpGet("camp_excelexport_veryshort")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> CampExcelexportVeryshort([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("camp_excelexport_veryshort", exportFormat);

    [HttpGet("camp_excelexport_short")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> CampExcelexportShort([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("camp_excelexport_short", exportFormat);

    [HttpGet("camp_excelexport_long")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> CampExcelexportLong([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("camp_excelexport_long", exportFormat);

    [HttpGet("camp_excelexport_daygroups")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> CampExcelexportDaygroups([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("camp_excelexport_daygroups", exportFormat);

    [HttpGet("camp_excelexport_roomies")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> CampExcelexportRoomies([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("camp_excelexport_roomies", exportFormat);

    [HttpGet("camp_excelexport_room_position")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> CampExcelexportRoomPosition([FromQuery] int exportFormat = 3)
        => CrystalReportAsync("camp_excelexport_room_position", exportFormat);
}
