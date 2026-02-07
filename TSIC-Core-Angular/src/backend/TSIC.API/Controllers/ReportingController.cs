using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Reporting;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos;

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

    public ReportingController(
        IReportingService reportingService,
        IJobLookupService jobLookupService)
    {
        _reportingService = reportingService;
        _jobLookupService = jobLookupService;
    }

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
    // Stored Procedure Excel Exports
    // ──────────────────────────────────────────────────────────────

    [HttpGet("export-sp")]
    [Authorize]
    public async Task<ActionResult> ExportStoredProcedureResults(
        [FromQuery] string spName,
        [FromQuery] bool bUseJobId,
        [FromQuery] bool bUseDateUnscheduled = false)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService) ?? Guid.Empty;
        var regId = User.GetRegistrationId();

        var result = await _reportingService.ExportStoredProcedureToExcelAsync(
            spName, jobId, bUseJobId, bUseDateUnscheduled);

        await _reportingService.RecordExportHistoryAsync(regId, spName, null);

        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    [HttpGet("export-monthly-reconciliation")]
    [Authorize]
    public async Task<ActionResult> ExportMonthlyReconciliationDataToExcel(
        [FromQuery] int settlementMonth,
        [FromQuery] int settlementYear)
    {
        var result = await _reportingService.ExportMonthlyReconciliationAsync(
            settlementMonth, settlementYear, isMerchandise: false);

        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    [HttpGet("export-monthly-reconciliation-merch")]
    [Authorize]
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

    [HttpGet("Get_JobPlayers_TSICDAILY")]
    public Task<ActionResult> GetJobPlayersTsicDaily()
        => CrystalReportAsync("JobPlayers_TSICDaily", 1);

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

    [HttpGet("AmericanSelectMainEventRosters")]
    [AllowAnonymous]
    public Task<ActionResult> AmericanSelectMainEventRosters([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("americanselectmaineventrosters", exportFormat);

    [HttpGet("AmericanSelectEvaluation")]
    [AllowAnonymous]
    public Task<ActionResult> AmericanSelectEvaluation([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("americanselectevaluation", exportFormat);

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

    [HttpGet("Schedule_Gamecards")]
    [AllowAnonymous]
    public Task<ActionResult> ScheduleGamecards([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("schedule_gamecards", exportFormat);

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

    [HttpGet("Get_Invoices_LastMonth")]
    [Authorize(Roles = "Superuser")]
    public Task<ActionResult> GetInvoicesLastMonth()
        => CrystalReportAsync("invoices2015", 1);

    [HttpGet("Get_Invoices_LastMonthSummariesOnly")]
    [Authorize(Roles = "Superuser")]
    public Task<ActionResult> GetInvoicesLastMonthSummariesOnly()
        => CrystalReportAsync("invoices2015SummariesOnly", 1);

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

    [HttpGet("TSICFeesYTDByCustomerAndJob")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> TsicFeesYtdByCustomerAndJob([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("tsicTSICFeesYTD", exportFormat);

    [HttpGet("TSICFeesYTDByCustomer")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> TsicFeesYtdByCustomer([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("tsicTSICFeesYTDByCustomer", exportFormat);

    [HttpGet("Get_NetUsers")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> GetNetUsers([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("netusers", exportFormat);

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

    [HttpGet("PlayerStats_E120")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> PlayerStatsE120()
        => CrystalReportAsync("PlayerStats_E120", 1);

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

    [HttpGet("ScheduleByClubAgTPerPage")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> ScheduleByClubAgTPerPage([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("ScheduleByClubAgTPerPage", exportFormat);

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

    [HttpGet("FieldUtilizationWithNominations")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> FieldUtilizationWithNominations([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("FieldUtilizationWithNominations", exportFormat);

    [HttpGet("TournamentRosterPacked_PositionSchool")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> TournamentRosterPackedPositionSchool([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("TournamentRosterPacked_PositionSchool", exportFormat);

    [HttpGet("TournamentRecruitingReport")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> TournamentRecruitingReport([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("TournamentRecruitingReport", exportFormat);

    [HttpGet("TournamentRecruitingReportASL")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> TournamentRecruitingReportAsl([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("TournamentRecruitingReportASL", exportFormat);

    [HttpGet("TournamentRecruitingReportUSL")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> TournamentRecruitingReportUsl([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("TournamentRecruitingReportUSL", exportFormat);

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

    [HttpGet("Schedule_ByAgegroup")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> ScheduleByAgegroup([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("Schedule_ByAgegroup", exportFormat);

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

    [HttpGet("camp_excelexport_summer_pdf")]
    [Authorize(Policy = "AdminOnly")]
    public Task<ActionResult> CampExcelexportSummerPdf([FromQuery] int exportFormat = 1)
        => CrystalReportAsync("camp_excelexport_summer_pdf", exportFormat);

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
