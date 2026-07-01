using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Syncfusion.XlsIO;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.API.Utilities;
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
        using var excelEngine = new ExcelEngine();
        IApplication application = excelEngine.Excel;
        application.DefaultVersion = ExcelVersion.Xlsx;
        IWorkbook workbook = application.Workbooks.Create(1);

        // ── Summary sheet (reuse the default sheet XlsIO creates) ──
        var summary = workbook.Worksheets[0];
        summary.Name = "Summary";
        summary.Range[1, 1].Text = "Schedule QA Report";
        summary.Range[1, 1].CellStyle.Font.Bold = true;
        summary.Range[2, 1].Text = $"Generated: {DateTime.Now:g}";
        summary.Range[3, 1].Text = $"Total Games: {qa.TotalGames}";
        summary.Range[5, 1].Text = "Check"; summary.Range[5, 2].Text = "Count"; summary.Range[5, 3].Text = "Severity";
        var sRow = 6;
        WriteCheckRow(summary, ref sRow, "Field Double Bookings", qa.FieldDoubleBookings.Count, "Critical");
        WriteCheckRow(summary, ref sRow, "Team Double Bookings", qa.TeamDoubleBookings.Count, "Critical");
        WriteCheckRow(summary, ref sRow, "Rank Mismatches", qa.RankMismatches.Count, "Critical");
        WriteCheckRow(summary, ref sRow, "Unscheduled Teams", qa.UnscheduledTeams.Count, "Warning");
        WriteCheckRow(summary, ref sRow, "Back-to-Back Games", qa.BackToBackGames.Count, "Warning");
        WriteCheckRow(summary, ref sRow, "Repeated Matchups", qa.RepeatedMatchups.Count, "Warning");
        WriteCheckRow(summary, ref sRow, "Inactive Teams in Games", qa.InactiveTeamsInGames.Count, "Warning");
        if (qa.BracketQa is { } bq)
        {
            var bracketErrors = bq.Findings.Count(f => f.Severity == "error");
            WriteCheckRow(summary, ref sRow, "Bracket Structural Errors", bracketErrors, "Critical");
            WriteCheckRow(summary, ref sRow, "Bracket Structural Warnings",
                bq.Findings.Count - bracketErrors, "Warning");
        }
        if (qa.CrossEventAnalysis != null)
        {
            sRow++;
            summary.Range[sRow, 1].Text = $"Cross-Event Analysis: {qa.CrossEventAnalysis.GroupName}";
            summary.Range[sRow, 1].CellStyle.Font.Bold = true;
            sRow++;
            WriteCheckRow(summary, ref sRow, "Club Overplay (across events)", qa.CrossEventAnalysis.ClubOverplay.Count, "Info");
            WriteCheckRow(summary, ref sRow, "Team Overplay (across events)", qa.CrossEventAnalysis.TeamOverplay.Count, "Warning");
        }
        summary.UsedRange.AutofitColumns();

        // ── Detail sheets (only if data exists) ──
        if (qa.FieldDoubleBookings.Count > 0)
            AddSheet(workbook, "Field Double Bookings", new[] { "Field", "Date/Time", "Count" },
                qa.FieldDoubleBookings.Select(i => new object[] { i.Label, i.GameDate, i.Count }));

        if (qa.TeamDoubleBookings.Count > 0)
            AddSheet(workbook, "Team Double Bookings", new[] { "Team", "Date/Time", "Count" },
                qa.TeamDoubleBookings.Select(i => new object[] { i.Label, i.GameDate, i.Count }));

        if (qa.RankMismatches.Count > 0)
            AddSheet(workbook, "Rank Mismatches", new[] { "AgeGroup", "Division", "Team", "Field", "GameTime", "SchedNo", "ActualRank" },
                qa.RankMismatches.Select(i => new object[] { i.AgegroupName, i.DivName, i.TeamName, i.FieldName, i.GameDate, i.ScheduleNo, i.ActualDivRank }));

        if (qa.UnscheduledTeams.Count > 0)
            AddSheet(workbook, "Unscheduled Teams", new[] { "AgeGroup", "Division", "Team", "Rank" },
                qa.UnscheduledTeams.Select(i => new object[] { i.AgegroupName, i.DivName, i.TeamName, i.DivRank }));

        if (qa.BackToBackGames.Count > 0)
            AddSheet(workbook, "Back-to-Back Games", new[] { "AgeGroup", "Division", "Team", "Field", "GameTime", "Gap (min)" },
                qa.BackToBackGames.Select(i => new object[] { i.AgegroupName, i.DivName, i.TeamName, i.FieldName, i.GameDate, i.MinutesSincePrevious }));

        if (qa.RepeatedMatchups.Count > 0)
            AddSheet(workbook, "Repeated Matchups", new[] { "AgeGroup", "Division", "Team1", "Team2", "TimesPlayed" },
                qa.RepeatedMatchups.Select(i => new object[] { i.AgegroupName, i.DivName, i.Team1Name, i.Team2Name, i.GameCount }));

        if (qa.InactiveTeamsInGames.Count > 0)
            AddSheet(workbook, "Inactive Teams", new[] { "AgeGroup", "Division", "Team", "Rank" },
                qa.InactiveTeamsInGames.Select(i => new object[] { i.AgegroupName, i.DivName, i.TeamName, i.DivRank }));

        // ── Informational sheets (always include) ──
        AddSheet(workbook, "Games Per Date", new[] { "Date", "Games" },
            qa.GamesPerDate.Select(i => new object[] { i.GameDay, i.GameCount }));

        AddSheet(workbook, "RR Games Per Division", new[] { "AgeGroup", "Division", "PoolSize", "Games" },
            qa.RrGamesPerDivision.Select(i => new object[] { i.AgegroupName, i.DivName, i.PoolSize, i.GameCount }));

        AddSheet(workbook, "Games Per Team", new[] { "AgeGroup", "Division", "Team", "Games" },
            qa.GamesPerTeam.Select(i => new object[] { i.AgegroupName, i.DivName, i.TeamName, i.GameCount }));

        AddSheet(workbook, "Games Per Team Per Day", new[] { "AgeGroup", "Division", "Club", "Team", "Day", "Games" },
            qa.GamesPerTeamPerDay.Select(i => new object[] { i.AgegroupName, i.DivName, i.ClubName, i.TeamName, i.GameDay, i.GameCount }));

        AddSheet(workbook, "Games Per Field Per Day", new[] { "Field", "Day", "Games" },
            qa.GamesPerFieldPerDay.Select(i => new object[] { i.FieldName, i.GameDay, i.GameCount }));

        AddSheet(workbook, "Game Spreads", new[] { "AgeGroup", "Division", "Team", "Day", "Games", "Spread (min)" },
            qa.GameSpreads.Select(i => new object[] { i.AgegroupName, i.DivName, i.TeamName, i.GameDay, i.GameCount, i.SpreadMinutes }));

        if (qa.BracketGames.Count > 0)
            AddSheet(workbook, "Bracket Games", new[] { "AgeGroup", "Field", "GameTime", "Slot1", "Slot2" },
                qa.BracketGames.Select(i => new object[] { i.AgegroupName, i.FieldName, i.GameDate, $"{i.T1Type}{i.T1No}", $"{i.T2Type}{i.T2No}" }));

        if (qa.BracketQa is { Findings.Count: > 0 } bracketQa)
            AddSheet(workbook, "Bracket QA", new[] { "Severity", "Category", "AgeGroup", "Division", "Game", "Detail" },
                bracketQa.Findings.Select(f => new object[]
                {
                    f.Severity, f.Category, f.AgegroupName, f.DivName,
                    f.Gid?.ToString() ?? "", f.Detail
                }));

        // ── Cross-Event sheets (only when job is in a comparison group) ──
        if (qa.CrossEventAnalysis is { } xEvt)
        {
            AddSheet(workbook, "XEvent Compared Events", new[] { "Event", "Abbreviation", "Games" },
                xEvt.ComparedEvents.Select(e => new object[] { e.JobName, e.Abbreviation, e.GameCount }));

            if (xEvt.ClubOverplay.Count > 0)
                AddSheet(workbook, "XEvent Club Overplay", new[] { "AgeGroup", "Club", "Opponent Club", "Matches" },
                    xEvt.ClubOverplay.Select(c => new object[] { c.Agegroup, c.TeamClub, c.OpponentClub, c.MatchCount }));

            if (xEvt.TeamOverplay.Count > 0)
                AddSheet(workbook, "XEvent Team Overplay", new[] { "AgeGroup", "Club", "Team", "Opp Club", "Opponent", "Matches", "Events" },
                    xEvt.TeamOverplay.Select(t => new object[] { t.Agegroup, t.TeamClub, t.TeamName, t.OpponentClub, t.OpponentName, t.MatchCount, t.Events }));
        }

        return workbook.ToByteArray();
    }

    private static void WriteCheckRow(IWorksheet ws, ref int row, string check, int count, string severity)
    {
        ws.Range[row, 1].Text = check;
        ws.Range[row, 2].Number = count;
        ws.Range[row, 3].Text = count == 0 ? "Pass" : severity;
        row++;
    }

    private static void AddSheet(IWorkbook workbook, string name,
        string[] headers, IEnumerable<object[]> rows)
    {
        var ws = workbook.Worksheets.Create(name);
        for (var c = 0; c < headers.Length; c++)
        {
            ws.Range[1, c + 1].Text = headers[c];
            ws.Range[1, c + 1].CellStyle.Font.Bold = true;
        }

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                ws.Range[r, c + 1].SetCellValue(row[c]);
                if (row[c] is DateTime)
                    ws.Range[r, c + 1].NumberFormat = "mm/dd/yyyy hh:mm";
            }
            r++;
        }

        if (ws.UsedRange != null)
            ws.UsedRange.AutofitColumns();
    }
}
