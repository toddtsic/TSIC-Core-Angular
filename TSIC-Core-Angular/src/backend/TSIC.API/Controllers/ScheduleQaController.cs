using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Schedule QA validation — standalone endpoint for the QA Results view.
/// Shared logic with Auto-Build post-validation via IScheduleQaService.
/// </summary>
[ApiController]
[Route("api/schedule-qa")]
[Authorize(Policy = "AdminOnly")]
public class ScheduleQaController : ControllerBase
{
    private readonly IScheduleQaService _qaService;
    private readonly IJobLookupService _jobLookupService;

    public ScheduleQaController(
        IScheduleQaService qaService,
        IJobLookupService jobLookupService)
    {
        _qaService = qaService;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// GET /api/schedule-qa/validate — Run all QA checks on the current job's schedule.
    /// </summary>
    [HttpGet("validate")]
    public async Task<ActionResult<AutoBuildQaResult>> Validate(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Scheduling context required" });

        var result = await _qaService.RunValidationAsync(jobId.Value, ct);
        return Ok(result);
    }

    /// <summary>
    /// GET /api/schedule-qa/export-excel — Run QA and return multi-sheet Excel workbook.
    /// Replaces the legacy [utility].[Schedule_QA_Tourny] sproc Excel export.
    /// </summary>
    [HttpGet("export-excel")]
    public async Task<IActionResult> ExportExcel(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Scheduling context required" });

        var qa = await _qaService.RunValidationAsync(jobId.Value, ct);
        var bytes = BuildQaExcel(qa);

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"ScheduleQA_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
    }

    private static byte[] BuildQaExcel(AutoBuildQaResult qa)
    {
        ExcelPackage.License.SetNonCommercialPersonal("Todd Greenwald");
        using var package = new ExcelPackage();

        // ── Summary sheet ──
        var summary = package.Workbook.Worksheets.Add("Summary");
        summary.Cells[1, 1].Value = "Schedule QA Report";
        summary.Cells[1, 1].Style.Font.Bold = true;
        summary.Cells[2, 1].Value = $"Generated: {DateTime.Now:g}";
        summary.Cells[3, 1].Value = $"Total Games: {qa.TotalGames}";
        summary.Cells[5, 1].Value = "Check"; summary.Cells[5, 2].Value = "Count"; summary.Cells[5, 3].Value = "Severity";
        var sRow = 6;
        WriteCheckRow(summary, ref sRow, "Field Double Bookings", qa.FieldDoubleBookings.Count, "Critical");
        WriteCheckRow(summary, ref sRow, "Team Double Bookings", qa.TeamDoubleBookings.Count, "Critical");
        WriteCheckRow(summary, ref sRow, "Rank Mismatches", qa.RankMismatches.Count, "Critical");
        WriteCheckRow(summary, ref sRow, "Unscheduled Teams", qa.UnscheduledTeams.Count, "Warning");
        WriteCheckRow(summary, ref sRow, "Back-to-Back Games", qa.BackToBackGames.Count, "Warning");
        WriteCheckRow(summary, ref sRow, "Repeated Matchups", qa.RepeatedMatchups.Count, "Warning");
        WriteCheckRow(summary, ref sRow, "Inactive Teams in Games", qa.InactiveTeamsInGames.Count, "Warning");
        if (qa.CrossEventAnalysis != null)
        {
            sRow++;
            summary.Cells[sRow, 1].Value = $"Cross-Event Analysis: {qa.CrossEventAnalysis.GroupName}";
            summary.Cells[sRow, 1].Style.Font.Bold = true;
            sRow++;
            WriteCheckRow(summary, ref sRow, "Club Overplay (across events)", qa.CrossEventAnalysis.ClubOverplay.Count, "Info");
            WriteCheckRow(summary, ref sRow, "Team Overplay (across events)", qa.CrossEventAnalysis.TeamOverplay.Count, "Warning");
        }
        summary.Cells[summary.Dimension.Address].AutoFitColumns();

        // ── Detail sheets (only if data exists) ──
        if (qa.FieldDoubleBookings.Count > 0)
            AddSheet(package, "Field Double Bookings", new[] { "Field", "Date/Time", "Count" },
                qa.FieldDoubleBookings.Select(i => new object[] { i.Label, i.GameDate, i.Count }));

        if (qa.TeamDoubleBookings.Count > 0)
            AddSheet(package, "Team Double Bookings", new[] { "Team", "Date/Time", "Count" },
                qa.TeamDoubleBookings.Select(i => new object[] { i.Label, i.GameDate, i.Count }));

        if (qa.RankMismatches.Count > 0)
            AddSheet(package, "Rank Mismatches", new[] { "AgeGroup", "Division", "Team", "Field", "GameTime", "SchedNo", "ActualRank" },
                qa.RankMismatches.Select(i => new object[] { i.AgegroupName, i.DivName, i.TeamName, i.FieldName, i.GameDate, i.ScheduleNo, i.ActualDivRank }));

        if (qa.UnscheduledTeams.Count > 0)
            AddSheet(package, "Unscheduled Teams", new[] { "AgeGroup", "Division", "Team", "Rank" },
                qa.UnscheduledTeams.Select(i => new object[] { i.AgegroupName, i.DivName, i.TeamName, i.DivRank }));

        if (qa.BackToBackGames.Count > 0)
            AddSheet(package, "Back-to-Back Games", new[] { "AgeGroup", "Division", "Team", "Field", "GameTime", "Gap (min)" },
                qa.BackToBackGames.Select(i => new object[] { i.AgegroupName, i.DivName, i.TeamName, i.FieldName, i.GameDate, i.MinutesSincePrevious }));

        if (qa.RepeatedMatchups.Count > 0)
            AddSheet(package, "Repeated Matchups", new[] { "AgeGroup", "Division", "Team1", "Team2", "TimesPlayed" },
                qa.RepeatedMatchups.Select(i => new object[] { i.AgegroupName, i.DivName, i.Team1Name, i.Team2Name, i.GameCount }));

        if (qa.InactiveTeamsInGames.Count > 0)
            AddSheet(package, "Inactive Teams", new[] { "AgeGroup", "Division", "Team", "Rank" },
                qa.InactiveTeamsInGames.Select(i => new object[] { i.AgegroupName, i.DivName, i.TeamName, i.DivRank }));

        // ── Informational sheets (always include) ──
        AddSheet(package, "Games Per Date", new[] { "Date", "Games" },
            qa.GamesPerDate.Select(i => new object[] { i.GameDay, i.GameCount }));

        AddSheet(package, "RR Games Per Division", new[] { "AgeGroup", "Division", "PoolSize", "Games" },
            qa.RrGamesPerDivision.Select(i => new object[] { i.AgegroupName, i.DivName, i.PoolSize, i.GameCount }));

        AddSheet(package, "Games Per Team", new[] { "AgeGroup", "Division", "Team", "Games" },
            qa.GamesPerTeam.Select(i => new object[] { i.AgegroupName, i.DivName, i.TeamName, i.GameCount }));

        AddSheet(package, "Games Per Team Per Day", new[] { "AgeGroup", "Division", "Club", "Team", "Day", "Games" },
            qa.GamesPerTeamPerDay.Select(i => new object[] { i.AgegroupName, i.DivName, i.ClubName, i.TeamName, i.GameDay, i.GameCount }));

        AddSheet(package, "Games Per Field Per Day", new[] { "Field", "Day", "Games" },
            qa.GamesPerFieldPerDay.Select(i => new object[] { i.FieldName, i.GameDay, i.GameCount }));

        AddSheet(package, "Game Spreads", new[] { "AgeGroup", "Division", "Team", "Day", "Games", "Spread (min)" },
            qa.GameSpreads.Select(i => new object[] { i.AgegroupName, i.DivName, i.TeamName, i.GameDay, i.GameCount, i.SpreadMinutes }));

        if (qa.BracketGames.Count > 0)
            AddSheet(package, "Bracket Games", new[] { "AgeGroup", "Field", "GameTime", "Slot1", "Slot2" },
                qa.BracketGames.Select(i => new object[] { i.AgegroupName, i.FieldName, i.GameDate, $"{i.T1Type}{i.T1No}", $"{i.T2Type}{i.T2No}" }));

        // ── Cross-Event sheets (only when job is in a comparison group) ──
        if (qa.CrossEventAnalysis is { } xEvt)
        {
            AddSheet(package, "XEvent Compared Events", new[] { "Event", "Abbreviation", "Games" },
                xEvt.ComparedEvents.Select(e => new object[] { e.JobName, e.Abbreviation, e.GameCount }));

            if (xEvt.ClubOverplay.Count > 0)
                AddSheet(package, "XEvent Club Overplay", new[] { "AgeGroup", "Club", "Opponent Club", "Matches" },
                    xEvt.ClubOverplay.Select(c => new object[] { c.Agegroup, c.TeamClub, c.OpponentClub, c.MatchCount }));

            if (xEvt.TeamOverplay.Count > 0)
                AddSheet(package, "XEvent Team Overplay", new[] { "AgeGroup", "Club", "Team", "Opp Club", "Opponent", "Matches", "Events" },
                    xEvt.TeamOverplay.Select(t => new object[] { t.Agegroup, t.TeamClub, t.TeamName, t.OpponentClub, t.OpponentName, t.MatchCount, t.Events }));
        }

        return package.GetAsByteArray();
    }

    private static void WriteCheckRow(ExcelWorksheet ws, ref int row, string check, int count, string severity)
    {
        ws.Cells[row, 1].Value = check;
        ws.Cells[row, 2].Value = count;
        ws.Cells[row, 3].Value = count == 0 ? "Pass" : severity;
        row++;
    }

    private static void AddSheet(ExcelPackage package, string name,
        string[] headers, IEnumerable<object[]> rows)
    {
        var ws = package.Workbook.Worksheets.Add(name);
        for (var c = 0; c < headers.Length; c++)
        {
            ws.Cells[1, c + 1].Value = headers[c];
            ws.Cells[1, c + 1].Style.Font.Bold = true;
        }

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                ws.Cells[r, c + 1].Value = row[c];
                if (row[c] is DateTime)
                    ws.Cells[r, c + 1].Style.Numberformat.Format = "mm/dd/yyyy hh:mm";
            }
            r++;
        }

        if (ws.Dimension != null)
            ws.Cells[ws.Dimension.Address].AutoFitColumns();
    }
}
