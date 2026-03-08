using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for Auto-Build Schedule: pattern extraction, division matching, field mapping.
/// </summary>
public sealed class AutoBuildRepository : IAutoBuildRepository
{
    private readonly SqlDbContext _context;

    public AutoBuildRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<GamePlacementPattern>> ExtractPatternAsync(
        Guid sourceJobId, CancellationToken ct = default)
    {
        // Get all scheduled games with a date
        var games = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == sourceJobId && s.GDate != null)
            .Select(s => new
            {
                s.AgegroupName,
                s.DivName,
                Rnd = (int)(s.Rnd ?? 0),
                GameNumber = s.GNo ?? 0,
                FieldName = s.FName ?? "",
                FieldId = s.FieldId ?? Guid.Empty,
                GDate = s.GDate!.Value,
                T1Type = s.T1Type ?? "T",
                T2Type = s.T2Type ?? "T",
                T1No = s.T1No,
                T2No = (int?)s.T2No
            })
            .OrderBy(s => s.GDate)
            .ToListAsync(ct);

        if (games.Count == 0)
            return [];

        // Compute day ordinals: assign 0-based ordinal to each distinct date
        var distinctDates = games
            .Select(g => g.GDate.Date)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var dayOrdinalMap = new Dictionary<DateTime, int>();
        for (var i = 0; i < distinctDates.Count; i++)
            dayOrdinalMap[distinctDates[i]] = i;

        return games.Select(g => new GamePlacementPattern
        {
            AgegroupName = g.AgegroupName ?? "",
            DivName = g.DivName ?? "",
            Rnd = g.Rnd,
            GameNumber = g.GameNumber,
            FieldName = g.FieldName,
            FieldId = g.FieldId,
            DayOfWeek = g.GDate.DayOfWeek,
            TimeOfDay = g.GDate.TimeOfDay,
            DayOrdinal = dayOrdinalMap[g.GDate.Date],
            T1Type = g.T1Type,
            T2Type = g.T2Type,
            T1No = g.T1No,
            T2No = g.T2No
        }).ToList();
    }

    public async Task<List<SourceDivisionSummary>> GetSourceDivisionSummariesAsync(
        Guid sourceJobId, CancellationToken ct = default)
    {
        // Get division summaries from the schedule, deriving team count from
        // the max T1No/T2No values (pairing numbers correspond to team ranks)
        var divGroups = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == sourceJobId
                        && s.GDate != null
                        && s.T1Type == "T"
                        && s.T2Type == "T")
            .GroupBy(s => new { s.AgegroupName, s.DivName })
            .Select(g => new
            {
                AgegroupName = g.Key.AgegroupName ?? "",
                DivName = g.Key.DivName ?? "",
                MaxT1No = g.Max(s => s.T1No ?? 0),
                MaxT2No = g.Max(s => (int)(s.T2No ?? 0)),
                GameCount = g.Count()
            })
            .ToListAsync(ct);

        return divGroups.Select(g => new SourceDivisionSummary
        {
            AgegroupName = g.AgegroupName,
            DivName = g.DivName,
            TeamCount = Math.Max(g.MaxT1No, g.MaxT2No),
            GameCount = g.GameCount
        }).ToList();
    }

    public async Task<List<CurrentDivisionSummary>> GetCurrentDivisionSummariesAsync(
        Guid currentJobId, CancellationToken ct = default)
    {
        // Get divisions via Teams → Division → Agegroup path
        // Count active teams per division
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == currentJobId
                        && t.Active == true
                        && t.DivId != null
                        && t.Div != null
                        && t.Agegroup != null)
            .GroupBy(t => new
            {
                AgegroupId = t.AgegroupId,
                AgegroupName = t.Agegroup!.AgegroupName,
                AgegroupColor = t.Agegroup!.Color,
                DivId = t.DivId!.Value,
                DivName = t.Div!.DivName
            })
            .Select(g => new CurrentDivisionSummary
            {
                AgegroupId = g.Key.AgegroupId,
                AgegroupName = g.Key.AgegroupName ?? "",
                AgegroupColor = g.Key.AgegroupColor,
                DivId = g.Key.DivId,
                DivName = g.Key.DivName ?? "",
                TeamCount = g.Count()
            })
            .OrderBy(d => d.AgegroupName)
            .ThenBy(d => d.DivName)
            .ToListAsync(ct);
    }

    public async Task<List<string>> GetSourceFieldNamesAsync(
        Guid sourceJobId, CancellationToken ct = default)
    {
        return await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == sourceJobId && s.GDate != null && s.FName != null)
            .Select(s => s.FName!)
            .Distinct()
            .OrderBy(f => f)
            .ToListAsync(ct);
    }

    public async Task<List<FieldNameMapping>> GetCurrentFieldsAsync(
        Guid leagueId, string season, CancellationToken ct = default)
    {
        // Fields assigned to the league-season via FieldsLeagueSeason join table
        return await _context.FieldsLeagueSeason
            .AsNoTracking()
            .Where(fls => fls.LeagueId == leagueId && fls.Season == season)
            .Select(fls => new FieldNameMapping
            {
                FieldId = fls.FieldId,
                FName = fls.Field!.FName ?? ""
            })
            .Distinct()
            .OrderBy(f => f.FName)
            .ToListAsync(ct);
    }

    public async Task<Dictionary<Guid, string>> GetFieldAddressesAsync(
        IEnumerable<Guid> fieldIds, CancellationToken ct = default)
    {
        var idList = fieldIds.ToList();
        if (idList.Count == 0)
            return new Dictionary<Guid, string>();

        var fields = await _context.Fields
            .AsNoTracking()
            .Where(f => idList.Contains(f.FieldId) && f.Address != null)
            .Select(f => new { f.FieldId, f.Address, f.City, f.Zip })
            .ToListAsync(ct);

        return fields
            .Where(f => !string.IsNullOrWhiteSpace(f.Address))
            .ToDictionary(
                f => f.FieldId,
                f => NormalizeAddress($"{f.Address}|{f.City}|{f.Zip}"));
    }

    /// <summary>
    /// Normalize an address string for comparison: lowercase, trim, strip periods/commas,
    /// collapse whitespace. "117 Evergreen Rd." and "117 Evergreen Rd" become identical.
    /// </summary>
    private static string NormalizeAddress(string raw)
    {
        var s = raw.ToLowerInvariant()
            .Replace(".", "")
            .Replace(",", "")
            .Trim();

        // collapse multiple spaces to single
        while (s.Contains("  "))
            s = s.Replace("  ", " ");

        // trim each segment individually
        var parts = s.Split('|');
        for (var i = 0; i < parts.Length; i++)
            parts[i] = parts[i].Trim();

        return string.Join("|", parts);
    }

    public async Task<Dictionary<Guid, int>> GetExistingGameCountsByDivisionAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.DivId != null && s.GDate != null)
            .GroupBy(s => s.DivId!.Value)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.Count(),
                ct);
    }

    public async Task<string?> GetJobNameAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.JobName)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<string?> GetJobYearAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.Year)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> DeleteAllGamesForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        // Delete cascade dependents first
        var gameIds = await _context.Schedule
            .Where(s => s.JobId == jobId)
            .Select(s => s.Gid)
            .ToListAsync(ct);

        if (gameIds.Count == 0)
            return 0;

        // Delete DeviceGids
        await _context.DeviceGids
            .Where(d => gameIds.Contains(d.Gid))
            .ExecuteDeleteAsync(ct);

        // Delete BracketSeeds
        await _context.BracketSeeds
            .Where(b => gameIds.Contains(b.Gid))
            .ExecuteDeleteAsync(ct);

        // Delete RefGameAssigments
        await _context.RefGameAssigments
            .Where(r => gameIds.Contains(r.GameId))
            .ExecuteDeleteAsync(ct);

        // Delete Schedule records
        var count = await _context.Schedule
            .Where(s => s.JobId == jobId)
            .ExecuteDeleteAsync(ct);

        return count;
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }

    // ── Prerequisite Checks ────────────────────────────────

    public async Task<int> GetUnassignedActiveTeamCountAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId
                        && t.Active == true
                        && t.DivId == null
                        && t.TeamName != "Unassigned"
                        && t.TeamName != "WAITLIST"
                        && t.TeamName != "DROPPED")
            .CountAsync(ct);
    }

    public async Task<List<string>> GetAgegroupsMissingTimeslotDatesAsync(
        Guid jobId, string season, string year, CancellationToken ct = default)
    {
        // Get agegroup IDs that have active teams with divisions
        // Exclude non-schedulable agegroups (WAITLIST, DROPPED) and placeholder divisions
        var agegroupIdsWithTeams = await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId
                        && t.Active == true
                        && t.DivId != null
                        && t.Agegroup != null
                        && !t.Agegroup!.AgegroupName!.StartsWith("WAITLIST")
                        && !t.Agegroup!.AgegroupName!.StartsWith("DROPPED")
                        && t.Div != null
                        && t.Div!.DivName != "Unassigned")
            .Select(t => t.AgegroupId)
            .Distinct()
            .ToListAsync(ct);

        // Get agegroup IDs that have timeslot dates
        var agegroupIdsWithDates = await _context.TimeslotsLeagueSeasonDates
            .AsNoTracking()
            .Where(d => d.Season == season && d.Year == year)
            .Select(d => d.AgegroupId)
            .Distinct()
            .ToListAsync(ct);

        // Find agegroup IDs with teams but no dates
        var missingIds = agegroupIdsWithTeams
            .Except(agegroupIdsWithDates)
            .ToHashSet();

        if (missingIds.Count == 0)
            return [];

        // Resolve agegroup names
        return await _context.Agegroups
            .AsNoTracking()
            .Where(a => missingIds.Contains(a.AgegroupId))
            .Select(a => a.AgegroupName ?? "")
            .OrderBy(n => n)
            .ToListAsync(ct);
    }

    // ── Post-Build QA Validation ────────────────────────────

    public async Task<AutoBuildQaResult> RunQaValidationAsync(
        Guid jobId, CancellationToken ct = default)
    {
        // Total games
        var totalGames = await _context.Schedule
            .AsNoTracking()
            .CountAsync(s => s.JobId == jobId && s.GDate != null, ct);

        // 1) Unscheduled teams — active teams with zero games
        var unscheduledTeams = await GetUnscheduledTeamsAsync(jobId, ct);

        // 2) Double bookings — field
        var fieldDoubleBookings = await GetFieldDoubleBookingsAsync(jobId, ct);

        // 3) Double bookings — team
        var teamDoubleBookings = await GetTeamDoubleBookingsAsync(jobId, ct);

        // 4) Rank mismatches — T1_No/T2_No != actual divRank
        var rankMismatches = await GetRankMismatchesAsync(jobId, ct);

        // 5) Back-to-backs — games too close together for same team
        var backToBackGames = await GetBackToBackGamesAsync(jobId, ct);

        // 6) Repeated matchups — same two teams playing > 1x
        var repeatedMatchups = await GetRepeatedMatchupsAsync(jobId, ct);

        // 7) Inactive teams in games
        var inactiveTeamsInGames = await GetInactiveTeamsInGamesAsync(jobId, ct);

        // 8) Games per date — overview
        var gamesPerDate = await GetGamesPerDateAsync(jobId, ct);

        // 9) Games per team — fairness check
        var gamesPerTeam = await GetGamesPerTeamAsync(jobId, ct);

        // 10) Games per team per day
        var gamesPerTeamPerDay = await GetGamesPerTeamPerDayAsync(jobId, ct);

        // 11) Games per field per day — utilization
        var gamesPerFieldPerDay = await GetGamesPerFieldPerDayAsync(jobId, ct);

        // 12) Game spreads — first-to-last per team per day
        var gameSpreads = await GetGameSpreadsAsync(jobId, ct);

        // 13) RR games per division — completeness
        var rrGamesPerDiv = await GetRrGamesPerDivAsync(jobId, ct);

        // 14) Bracket games listing
        var bracketGames = await GetBracketGamesAsync(jobId, ct);

        // 15) Teams below game guarantee
        var teamsBelowGuarantee = await GetTeamsBelowGuaranteeAsync(jobId, gamesPerTeam, ct);

        return new AutoBuildQaResult
        {
            TotalGames = totalGames,
            UnscheduledTeams = unscheduledTeams,
            FieldDoubleBookings = fieldDoubleBookings,
            TeamDoubleBookings = teamDoubleBookings,
            RankMismatches = rankMismatches,
            BackToBackGames = backToBackGames,
            RepeatedMatchups = repeatedMatchups,
            InactiveTeamsInGames = inactiveTeamsInGames,
            TeamsBelowGuarantee = teamsBelowGuarantee,
            GamesPerDate = gamesPerDate,
            GamesPerTeam = gamesPerTeam,
            GamesPerTeamPerDay = gamesPerTeamPerDay,
            GamesPerFieldPerDay = gamesPerFieldPerDay,
            GameSpreads = gameSpreads,
            RrGamesPerDivision = rrGamesPerDiv,
            BracketGames = bracketGames
        };
    }

    private async Task<List<QaUnscheduledTeam>> GetUnscheduledTeamsAsync(
        Guid jobId, CancellationToken ct)
    {
        // Active teams that don't appear in any scheduled game
        var scheduled = _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate != null);

        var scheduledTeamIds = await scheduled
            .Where(s => s.T1Id != null).Select(s => s.T1Id!.Value)
            .Union(scheduled.Where(s => s.T2Id != null).Select(s => s.T2Id!.Value))
            .Distinct()
            .ToListAsync(ct);

        var scheduledSet = new HashSet<Guid>(scheduledTeamIds);

        // Must match AutoBuildScheduleService.FilterSchedulableDivisions exclusions
        var allActiveTeams = await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId
                        && t.Active == true
                        && t.DivId != null
                        && t.Div != null
                        && t.Agegroup != null
                        && !t.Div!.DivName!.Contains("Unassigned")
                        && !t.Div!.DivName!.StartsWith("DROPPED")
                        && !t.Div!.DivName!.Contains("Dropped")
                        && !t.Agegroup!.AgegroupName!.Contains("WAITLIST")
                        && !t.Agegroup!.AgegroupName!.Contains("Waitlist")
                        && !t.Agegroup!.AgegroupName!.Contains("DROPPED")
                        && !t.Agegroup!.AgegroupName!.Contains("Dropped"))
            .Select(t => new
            {
                t.TeamId,
                AgegroupName = t.Agegroup!.AgegroupName ?? "",
                DivName = t.Div!.DivName ?? "",
                TeamName = t.TeamName ?? "",
                DivRank = t.DivRank
            })
            .ToListAsync(ct);

        return allActiveTeams
            .Where(t => !scheduledSet.Contains(t.TeamId))
            .Select(t => new QaUnscheduledTeam
            {
                AgegroupName = t.AgegroupName,
                DivName = t.DivName,
                TeamName = t.TeamName,
                DivRank = t.DivRank
            })
            .OrderBy(t => t.AgegroupName)
            .ThenBy(t => t.DivName)
            .ToList();
    }

    private async Task<List<QaTeamBelowGuarantee>> GetTeamsBelowGuaranteeAsync(
        Guid jobId, List<QaGamesPerTeam> gamesPerTeam, CancellationToken ct)
    {
        // Resolve guarantee cascade: agegroup.GameGuarantee ?? job.GameGuarantee
        var jobGuarantee = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.GameGuarantee)
            .FirstOrDefaultAsync(ct);

        if (jobGuarantee == null)
            return []; // No guarantee configured — nothing to check

        // Build agegroup name → resolved guarantee map
        var leagueId = await _context.JobLeagues
            .AsNoTracking()
            .Where(jl => jl.JobId == jobId)
            .Select(jl => jl.LeagueId)
            .FirstOrDefaultAsync(ct);

        var agOverrides = await _context.Agegroups
            .AsNoTracking()
            .Where(a => a.LeagueId == leagueId && a.GameGuarantee != null)
            .Select(a => new { a.AgegroupName, a.GameGuarantee })
            .ToListAsync(ct);

        var agGuaranteeByName = agOverrides.ToDictionary(
            a => a.AgegroupName ?? "", a => a.GameGuarantee!.Value);

        return gamesPerTeam
            .Select(t =>
            {
                var guarantee = agGuaranteeByName.GetValueOrDefault(t.AgegroupName, jobGuarantee.Value);
                return new { t.AgegroupName, t.DivName, t.TeamName, t.GameCount, Guarantee = guarantee };
            })
            .Where(t => t.GameCount < t.Guarantee)
            .Select(t => new QaTeamBelowGuarantee
            {
                AgegroupName = t.AgegroupName,
                DivName = t.DivName,
                TeamName = t.TeamName,
                GameCount = t.GameCount,
                Guarantee = t.Guarantee
            })
            .OrderBy(t => t.AgegroupName)
            .ThenBy(t => t.DivName)
            .ThenBy(t => t.TeamName)
            .ToList();
    }

    private async Task<List<QaDoubleBooking>> GetFieldDoubleBookingsAsync(
        Guid jobId, CancellationToken ct)
    {
        return await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate != null && s.FieldId != null)
            .GroupBy(s => new { s.GDate, s.FieldId })
            .Where(g => g.Count() > 1)
            .Select(g => new QaDoubleBooking
            {
                Label = g.First().FName ?? g.Key.FieldId.ToString()!,
                GameDate = g.Key.GDate!.Value,
                Count = g.Count()
            })
            .OrderBy(d => d.GameDate)
            .ToListAsync(ct);
    }

    private async Task<List<QaDoubleBooking>> GetTeamDoubleBookingsAsync(
        Guid jobId, CancellationToken ct)
    {
        // Get all games with team IDs
        var games = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate != null)
            .Select(s => new
            {
                s.GDate,
                s.T1Id,
                T1Name = s.T1Name ?? "",
                s.T2Id,
                T2Name = s.T2Name ?? ""
            })
            .ToListAsync(ct);

        // Flatten to per-team-per-datetime, find duplicates
        var teamGames = games
            .SelectMany(g => new[]
            {
                new { TeamId = g.T1Id, TeamName = g.T1Name, g.GDate },
                new { TeamId = g.T2Id, TeamName = g.T2Name, g.GDate }
            })
            .Where(tg => tg.TeamId != null)
            .GroupBy(tg => new { tg.TeamId, tg.GDate })
            .Where(g => g.Count() > 1)
            .Select(g => new QaDoubleBooking
            {
                Label = g.First().TeamName,
                GameDate = g.Key.GDate!.Value,
                Count = g.Count()
            })
            .OrderBy(d => d.GameDate)
            .ToList();

        return teamGames;
    }

    private async Task<List<QaBackToBack>> GetBackToBackGamesAsync(
        Guid jobId, CancellationToken ct)
    {
        // ── Step 1: Determine the BTB threshold from timeslot config ──
        // Legacy logic: MAX(gamestartInterval) per division, then MAX across all.
        // gamestartInterval = minutes between game starts on a field (game length + transition).
        // A BTB means the team's next game starts within that interval — no rest period.
        var agegroupIds = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.AgegroupId != null)
            .Select(s => s.AgegroupId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var maxGsi = 0;
        if (agegroupIds.Count > 0)
        {
            var intervals = await _context.TimeslotsLeagueSeasonFields
                .AsNoTracking()
                .Where(t => agegroupIds.Contains(t.AgegroupId))
                .Select(t => new { t.DivId, t.GamestartInterval })
                .ToListAsync(ct);

            if (intervals.Count > 0)
            {
                // Max interval per division, then overall max (matches legacy sproc)
                maxGsi = intervals
                    .GroupBy(x => x.DivId)
                    .Select(g => g.Max(x => x.GamestartInterval))
                    .Max();
            }
        }

        if (maxGsi <= 0)
            return []; // No timeslot config — can't determine BTB threshold

        // ── Step 2: Get all RR games ──
        var games = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate != null
                        && s.T1Type == "T" && s.T2Type == "T")
            .Select(s => new
            {
                s.T1Id, T1Name = s.T1Name ?? "",
                s.T2Id, T2Name = s.T2Name ?? "",
                s.GDate,
                FieldName = s.FName ?? "",
                AgegroupName = s.AgegroupName ?? "",
                DivName = s.DivName ?? ""
            })
            .ToListAsync(ct);

        // ── Step 3: Flatten to per-team records, sorted by team + time ──
        var teamGames = games
            .Where(g => g.GDate != null)
            .SelectMany(g => new[]
            {
                new { TeamId = g.T1Id, TeamName = g.T1Name, g.GDate, g.FieldName, g.AgegroupName, g.DivName },
                new { TeamId = g.T2Id, TeamName = g.T2Name, g.GDate, g.FieldName, g.AgegroupName, g.DivName }
            })
            .Where(tg => tg.TeamId != null)
            .OrderBy(tg => tg.TeamId)
            .ThenBy(tg => tg.GDate)
            .ToList();

        // ── Step 4: Detect BTBs — same team, next game within maxGSI minutes ──
        var backToBacks = new List<QaBackToBack>();
        for (var i = 1; i < teamGames.Count; i++)
        {
            var prev = teamGames[i - 1];
            var curr = teamGames[i];

            if (prev.TeamId != curr.TeamId) continue;

            var minutes = (int)(curr.GDate!.Value - prev.GDate!.Value).TotalMinutes;
            if (minutes <= maxGsi)
            {
                backToBacks.Add(new QaBackToBack
                {
                    AgegroupName = curr.AgegroupName,
                    DivName = curr.DivName,
                    TeamName = curr.TeamName,
                    FieldName = curr.FieldName,
                    GameDate = curr.GDate!.Value,
                    MinutesSincePrevious = minutes,
                    SlotIndex = maxGsi // repurpose: report the threshold used
                });
            }
        }

        return backToBacks
            .OrderBy(b => b.GameDate)
            .ThenBy(b => b.TeamName)
            .ToList();
    }

    private async Task<List<QaGamesPerTeam>> GetGamesPerTeamAsync(
        Guid jobId, CancellationToken ct)
    {
        var games = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate != null
                        && s.T1Type == "T" && s.T2Type == "T")
            .Select(s => new
            {
                s.T1Id, T1Name = s.T1Name ?? "",
                s.T2Id, T2Name = s.T2Name ?? "",
                AgegroupName = s.AgegroupName ?? "",
                DivName = s.DivName ?? ""
            })
            .ToListAsync(ct);

        var teamCounts = games
            .SelectMany(g => new[]
            {
                new { TeamId = g.T1Id, TeamName = g.T1Name, g.AgegroupName, g.DivName },
                new { TeamId = g.T2Id, TeamName = g.T2Name, g.AgegroupName, g.DivName }
            })
            .Where(t => t.TeamId != null)
            .GroupBy(t => new { t.TeamId, t.TeamName, t.AgegroupName, t.DivName })
            .Select(g => new QaGamesPerTeam
            {
                AgegroupName = g.Key.AgegroupName,
                DivName = g.Key.DivName,
                TeamName = g.Key.TeamName,
                GameCount = g.Count()
            })
            .OrderBy(t => t.AgegroupName)
            .ThenBy(t => t.DivName)
            .ThenBy(t => t.TeamName)
            .ToList();

        return teamCounts;
    }

    private async Task<List<QaGameSpread>> GetGameSpreadsAsync(
        Guid jobId, CancellationToken ct)
    {
        var games = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate != null
                        && s.T1Type == "T" && s.T2Type == "T")
            .Select(s => new
            {
                s.T1Id, T1Name = s.T1Name ?? "",
                s.T2Id, T2Name = s.T2Name ?? "",
                s.GDate,
                AgegroupName = s.AgegroupName ?? "",
                DivName = s.DivName ?? ""
            })
            .ToListAsync(ct);

        var teamGames = games
            .SelectMany(g => new[]
            {
                new { TeamId = g.T1Id, TeamName = g.T1Name, g.GDate, g.AgegroupName, g.DivName },
                new { TeamId = g.T2Id, TeamName = g.T2Name, g.GDate, g.AgegroupName, g.DivName }
            })
            .Where(t => t.TeamId != null && t.GDate != null);

        var spreads = teamGames
            .GroupBy(t => new
            {
                t.TeamId,
                t.TeamName,
                t.AgegroupName,
                t.DivName,
                GameDay = t.GDate!.Value.Date
            })
            .Where(g => g.Count() > 1)
            .Select(g =>
            {
                var minDate = g.Min(x => x.GDate!.Value);
                var maxDate = g.Max(x => x.GDate!.Value);
                return new QaGameSpread
                {
                    AgegroupName = g.Key.AgegroupName,
                    DivName = g.Key.DivName,
                    TeamName = g.Key.TeamName,
                    GameDay = g.Key.GameDay.ToString("MM/dd/yyyy"),
                    SpreadMinutes = (int)(maxDate - minDate).TotalMinutes,
                    GameCount = g.Count()
                };
            })
            .OrderBy(s => s.AgegroupName)
            .ThenBy(s => s.DivName)
            .ThenBy(s => s.TeamName)
            .ToList();

        return spreads;
    }

    // ── Check 4: Rank Mismatches ────────────────────────────────
    private async Task<List<QaRankMismatch>> GetRankMismatchesAsync(
        Guid jobId, CancellationToken ct)
    {
        // Schedule rows where T1/T2 are real teams ('T') but T1_No/T2_No
        // doesn't match the team's actual divRank.
        var games = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate != null
                        && s.T1Type == "T" && s.T2Type == "T"
                        && s.T1Id != null && s.T2Id != null)
            .Select(s => new
            {
                s.T1Id, s.T1No, T1Name = s.T1Name ?? "",
                s.T2Id, T2No = (int?)s.T2No, T2Name = s.T2Name ?? "",
                s.GDate,
                FieldName = s.FName ?? "",
                AgegroupName = s.AgegroupName ?? "",
                DivName = s.DivName ?? ""
            })
            .ToListAsync(ct);

        var teamIds = games
            .SelectMany(g => new[] { g.T1Id!.Value, g.T2Id!.Value })
            .Distinct()
            .ToList();

        var teamRanks = await _context.Teams
            .AsNoTracking()
            .Where(t => teamIds.Contains(t.TeamId))
            .Select(t => new { t.TeamId, t.DivRank })
            .ToDictionaryAsync(t => t.TeamId, t => t.DivRank, ct);

        var mismatches = new List<QaRankMismatch>();
        foreach (var g in games)
        {
            if (teamRanks.TryGetValue(g.T1Id!.Value, out var t1Rank) && g.T1No != t1Rank)
            {
                mismatches.Add(new QaRankMismatch
                {
                    AgegroupName = g.AgegroupName,
                    DivName = g.DivName,
                    FieldName = g.FieldName,
                    GameDate = g.GDate!.Value,
                    TeamName = g.T1Name,
                    ScheduleNo = g.T1No ?? 0,
                    ActualDivRank = t1Rank
                });
            }
            if (teamRanks.TryGetValue(g.T2Id!.Value, out var t2Rank) && g.T2No != t2Rank)
            {
                mismatches.Add(new QaRankMismatch
                {
                    AgegroupName = g.AgegroupName,
                    DivName = g.DivName,
                    FieldName = g.FieldName,
                    GameDate = g.GDate!.Value,
                    TeamName = g.T2Name,
                    ScheduleNo = g.T2No ?? 0,
                    ActualDivRank = t2Rank
                });
            }
        }

        return mismatches
            .OrderBy(m => m.AgegroupName)
            .ThenBy(m => m.DivName)
            .ThenBy(m => m.GameDate)
            .ToList();
    }

    // ── Check 6: Repeated Matchups ──────────────────────────────
    private async Task<List<QaRepeatedMatchup>> GetRepeatedMatchupsAsync(
        Guid jobId, CancellationToken ct)
    {
        var games = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate != null
                        && s.T1Type == "T" && s.T2Type == "T"
                        && s.T1Id != null && s.T2Id != null)
            .Select(s => new
            {
                s.T1Id, T1Name = s.T1Name ?? "",
                s.T2Id, T2Name = s.T2Name ?? "",
                AgegroupName = s.AgegroupName ?? "",
                DivName = s.DivName ?? ""
            })
            .ToListAsync(ct);

        // Normalize matchup key so (A vs B) == (B vs A)
        return games
            .Select(g =>
            {
                var ids = new[] { g.T1Id!.Value, g.T2Id!.Value }.OrderBy(id => id).ToArray();
                return new
                {
                    PairKey = (ids[0], ids[1]),
                    Team1Name = g.T1Id!.Value == ids[0] ? g.T1Name : g.T2Name,
                    Team2Name = g.T1Id!.Value == ids[0] ? g.T2Name : g.T1Name,
                    g.AgegroupName,
                    g.DivName
                };
            })
            .GroupBy(g => g.PairKey)
            .Where(g => g.Count() > 1)
            .Select(g => new QaRepeatedMatchup
            {
                AgegroupName = g.First().AgegroupName,
                DivName = g.First().DivName,
                Team1Name = g.First().Team1Name,
                Team2Name = g.First().Team2Name,
                GameCount = g.Count()
            })
            .OrderBy(m => m.AgegroupName)
            .ThenBy(m => m.DivName)
            .ToList();
    }

    // ── Check 7: Inactive Teams in Games ────────────────────────
    private async Task<List<QaInactiveTeamInGame>> GetInactiveTeamsInGamesAsync(
        Guid jobId, CancellationToken ct)
    {
        var rrGames = _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate != null
                        && s.T1Type == "T" && s.T2Type == "T");

        var scheduledTeamIds = await rrGames
            .Where(s => s.T1Id != null).Select(s => s.T1Id!.Value)
            .Union(rrGames.Where(s => s.T2Id != null).Select(s => s.T2Id!.Value))
            .Distinct()
            .ToListAsync(ct);

        var scheduledSet = new HashSet<Guid>(scheduledTeamIds);

        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId
                        && t.Active != true
                        && scheduledSet.Contains(t.TeamId)
                        && t.Agegroup != null && t.Div != null)
            .Select(t => new QaInactiveTeamInGame
            {
                AgegroupName = t.Agegroup!.AgegroupName ?? "",
                DivName = t.Div!.DivName ?? "",
                TeamName = t.TeamName ?? "",
                DivRank = t.DivRank,
                Active = t.Active ?? false
            })
            .OrderBy(t => t.AgegroupName)
            .ThenBy(t => t.DivName)
            .ToListAsync(ct);
    }

    // ── Check 8: Games Per Date ─────────────────────────────────
    private async Task<List<QaGamesPerDate>> GetGamesPerDateAsync(
        Guid jobId, CancellationToken ct)
    {
        var games = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate != null)
            .Select(s => s.GDate!.Value)
            .ToListAsync(ct);

        return games
            .GroupBy(d => d.Date)
            .Select(g => new QaGamesPerDate
            {
                GameDay = g.Key.ToString("MM/dd/yyyy"),
                GameCount = g.Count()
            })
            .OrderBy(d => d.GameDay)
            .ToList();
    }

    // ── Check 10: Games Per Team Per Day ─────────────────────────
    private async Task<List<QaGamesPerTeamPerDay>> GetGamesPerTeamPerDayAsync(
        Guid jobId, CancellationToken ct)
    {
        var games = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate != null
                        && s.T1Type == "T" && s.T2Type == "T")
            .Select(s => new
            {
                s.T1Id, T1Name = s.T1Name ?? "",
                s.T2Id, T2Name = s.T2Name ?? "",
                s.GDate,
                AgegroupName = s.AgegroupName ?? "",
                DivName = s.DivName ?? ""
            })
            .ToListAsync(ct);

        // Need club names — look up via Teams → Registration
        var teamIds = games
            .SelectMany(g => new[] { g.T1Id, g.T2Id })
            .Where(id => id != null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var teamClubs = await _context.Teams
            .AsNoTracking()
            .Where(t => teamIds.Contains(t.TeamId) && t.ClubrepRegistration != null)
            .Select(t => new { t.TeamId, ClubName = t.ClubrepRegistration!.ClubName ?? "" })
            .ToDictionaryAsync(t => t.TeamId, t => t.ClubName, ct);

        var teamGames = games
            .SelectMany(g => new[]
            {
                new { TeamId = g.T1Id, TeamName = g.T1Name, g.GDate, g.AgegroupName, g.DivName },
                new { TeamId = g.T2Id, TeamName = g.T2Name, g.GDate, g.AgegroupName, g.DivName }
            })
            .Where(t => t.TeamId != null && t.GDate != null);

        return teamGames
            .GroupBy(t => new
            {
                t.TeamId,
                t.TeamName,
                t.AgegroupName,
                t.DivName,
                GameDay = t.GDate!.Value.Date
            })
            .Select(g => new QaGamesPerTeamPerDay
            {
                AgegroupName = g.Key.AgegroupName,
                DivName = g.Key.DivName,
                ClubName = teamClubs.GetValueOrDefault(g.Key.TeamId!.Value, ""),
                TeamName = g.Key.TeamName,
                GameDay = g.Key.GameDay.ToString("MM/dd/yyyy"),
                GameCount = g.Count()
            })
            .OrderBy(t => t.AgegroupName)
            .ThenBy(t => t.DivName)
            .ThenBy(t => t.ClubName)
            .ThenBy(t => t.TeamName)
            .ThenBy(t => t.GameDay)
            .ToList();
    }

    // ── Check 11: Games Per Field Per Day ────────────────────────
    private async Task<List<QaGamesPerFieldPerDay>> GetGamesPerFieldPerDayAsync(
        Guid jobId, CancellationToken ct)
    {
        var games = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate != null && s.FieldId != null)
            .Select(s => new { s.GDate, FieldName = s.FName ?? "" })
            .ToListAsync(ct);

        return games
            .GroupBy(g => new { g.FieldName, GameDay = g.GDate!.Value.Date })
            .Select(g => new QaGamesPerFieldPerDay
            {
                FieldName = g.Key.FieldName,
                GameDay = g.Key.GameDay.ToString("MM/dd/yyyy"),
                GameCount = g.Count()
            })
            .OrderBy(f => f.FieldName)
            .ThenBy(f => f.GameDay)
            .ToList();
    }

    // ── Check 13: RR Games Per Division ──────────────────────────
    private async Task<List<QaRrGamesPerDiv>> GetRrGamesPerDivAsync(
        Guid jobId, CancellationToken ct)
    {
        // Count distinct teams per division and distinct RR games per division
        var rrGames = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate != null
                        && s.T1Type == "T" && s.T2Type == "T"
                        && s.DivId != null && s.AgegroupId != null)
            .Select(s => new
            {
                s.Gid,
                s.DivId,
                s.AgegroupId,
                AgegroupName = s.AgegroupName ?? "",
                DivName = s.DivName ?? ""
            })
            .ToListAsync(ct);

        var gamesByDiv = rrGames
            .GroupBy(s => new { s.DivId, s.AgegroupName, s.DivName })
            .Select(g => new { g.Key.DivId, g.Key.AgegroupName, g.Key.DivName, GameCount = g.Select(x => x.Gid).Distinct().Count() })
            .ToList();

        // Get pool sizes (active teams per division)
        var divIds = gamesByDiv.Select(d => d.DivId!.Value).Distinct().ToList();
        var poolSizes = await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId && t.Active == true && t.DivId != null && divIds.Contains(t.DivId!.Value))
            .GroupBy(t => t.DivId)
            .Select(g => new { DivId = g.Key, PoolSize = g.Count() })
            .ToDictionaryAsync(g => g.DivId!.Value, g => g.PoolSize, ct);

        return gamesByDiv
            .Select(g => new QaRrGamesPerDiv
            {
                AgegroupName = g.AgegroupName,
                DivName = g.DivName,
                PoolSize = poolSizes.GetValueOrDefault(g.DivId!.Value, 0),
                GameCount = g.GameCount
            })
            .OrderBy(d => d.PoolSize)
            .ThenBy(d => d.GameCount)
            .ToList();
    }

    // ── Check 14: Bracket/Playoff Games ─────────────────────────
    private async Task<List<QaBracketGame>> GetBracketGamesAsync(
        Guid jobId, CancellationToken ct)
    {
        return await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate != null
                        && (s.T1Type != "T" || s.T2Type != "T"))
            .Select(s => new QaBracketGame
            {
                AgegroupName = s.AgegroupName ?? "",
                FieldName = s.FName ?? "",
                GameDate = s.GDate!.Value,
                T1Type = s.T1Type ?? "",
                T1No = s.T1No ?? 0,
                T2Type = s.T2Type ?? "",
                T2No = (int)(s.T2No ?? 0)
            })
            .OrderBy(b => b.AgegroupName)
            .ThenBy(b => b.GameDate)
            .ToListAsync(ct);
    }

    // ── Cross-Event Analysis ──────────────────────────────────

    public async Task<List<(Guid JobId, string JobName)>> FindJobsByNamePatternsAsync(
        IEnumerable<string> namePatterns, CancellationToken ct = default)
    {
        var patterns = namePatterns.ToList();

        // Get jobs that have at least one scheduled RR game
        var jobsWithGames = _context.Schedule
            .AsNoTracking()
            .Where(s => s.GDate != null && s.T1Type == "T" && s.T2Type == "T")
            .Select(s => s.JobId)
            .Distinct();

        var jobs = await _context.Jobs
            .AsNoTracking()
            .Where(j => jobsWithGames.Contains(j.JobId))
            .Select(j => new { j.JobId, j.JobName })
            .ToListAsync(ct);

        // Filter in-memory: job name contains any of the patterns (case-insensitive)
        return jobs
            .Where(j => j.JobName != null
                        && patterns.Any(p => j.JobName.Contains(p, StringComparison.OrdinalIgnoreCase)))
            .Select(j => (j.JobId, j.JobName!))
            .ToList();
    }

    public async Task<List<CrossEventMatchupRaw>> GetCrossEventMatchupsAsync(
        IEnumerable<Guid> jobIds, CancellationToken ct = default)
    {
        var jobIdList = jobIds.ToList();

        // Get all RR games across compared jobs
        var games = await _context.Schedule
            .AsNoTracking()
            .Where(s => jobIdList.Contains(s.JobId)
                        && s.GDate != null
                        && s.T1Type == "T" && s.T2Type == "T"
                        && s.T1Id != null && s.T2Id != null)
            .Select(s => new
            {
                s.JobId,
                s.T1Id, s.T2Id,
                T1Name = s.T1Name ?? "",
                T2Name = s.T2Name ?? "",
                AgegroupName = s.AgegroupName ?? ""
            })
            .ToListAsync(ct);

        // Get club names for all involved teams
        var teamIds = games
            .SelectMany(g => new[] { g.T1Id!.Value, g.T2Id!.Value })
            .Distinct()
            .ToList();

        var teamClubs = await _context.Teams
            .AsNoTracking()
            .Where(t => teamIds.Contains(t.TeamId) && t.ClubrepRegistration != null)
            .Select(t => new { t.TeamId, ClubName = t.ClubrepRegistration!.ClubName ?? "" })
            .ToDictionaryAsync(t => t.TeamId, t => t.ClubName, ct);

        // Flatten: each game produces TWO records (one per team perspective)
        var matchups = new List<CrossEventMatchupRaw>();
        foreach (var g in games)
        {
            var t1Club = teamClubs.GetValueOrDefault(g.T1Id!.Value, "");
            var t2Club = teamClubs.GetValueOrDefault(g.T2Id!.Value, "");

            matchups.Add(new CrossEventMatchupRaw
            {
                Agegroup = g.AgegroupName,
                TeamClub = t1Club, TeamName = g.T1Name,
                OpponentClub = t2Club, OpponentName = g.T2Name,
                JobId = g.JobId
            });
            matchups.Add(new CrossEventMatchupRaw
            {
                Agegroup = g.AgegroupName,
                TeamClub = t2Club, TeamName = g.T2Name,
                OpponentClub = t1Club, OpponentName = g.T1Name,
                JobId = g.JobId
            });
        }

        return matchups;
    }

    // ── Source Preconfiguration (returning tournament carry-forward) ──

    public async Task<List<SourceAgegroupMeta>> GetSourceAgegroupMetaAsync(
        Guid sourceJobId, CancellationToken ct = default)
    {
        // Join Schedule → Agegroups to get structured fields (Color, GradYear)
        // not available in the denormalized schedule table.
        var raw = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == sourceJobId && s.AgegroupId != null && s.Agegroup != null)
            .Select(s => new
            {
                s.AgegroupId,
                AgName = s.AgegroupName ?? "",
                s.Agegroup!.Color,
                s.Agegroup!.GradYearMin,
                s.Agegroup!.GradYearMax
            })
            .Distinct()
            .ToListAsync(ct);

        // Deduplicate by agegroup name (in case multiple AgegroupId rows have same name)
        return raw
            .GroupBy(r => r.AgName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(r => new SourceAgegroupMeta
            {
                AgegroupName = r.AgName,
                Color = r.Color,
                GradYearMin = r.GradYearMin,
                GradYearMax = r.GradYearMax
            })
            .ToList();
    }

    public async Task<Dictionary<string, List<SourceDateEntry>>> GetSourceDatesAsync(
        Guid sourceJobId, CancellationToken ct = default)
    {
        // Get the source job's season/year
        var job = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == sourceJobId)
            .Select(j => new { j.Season, j.Year })
            .FirstOrDefaultAsync(ct);

        if (job?.Season == null || job.Year == null)
            return new Dictionary<string, List<SourceDateEntry>>();

        // Get agegroup IDs that were actually scheduled in the source job
        var agIds = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == sourceJobId && s.AgegroupId != null)
            .Select(s => s.AgegroupId!.Value)
            .Distinct()
            .ToListAsync(ct);

        if (agIds.Count == 0)
            return new Dictionary<string, List<SourceDateEntry>>();

        // Try TimeslotsLeagueSeasonDates first (configuration table)
        var dateRows = await _context.TimeslotsLeagueSeasonDates
            .AsNoTracking()
            .Where(d => agIds.Contains(d.AgegroupId)
                && d.Season == job.Season && d.Year == job.Year)
            .Select(d => new { AgName = d.Agegroup.AgegroupName ?? "", d.GDate, d.Rnd })
            .ToListAsync(ct);

        // Fallback: extract dates from actual schedule data (always populated for built schedules)
        if (dateRows.Count == 0)
        {
            var scheduleRows = await _context.Schedule
                .AsNoTracking()
                .Where(s => s.JobId == sourceJobId
                    && s.GDate != null && s.AgegroupId != null
                    && s.T1Type == "T" && s.T2Type == "T")
                .Select(s => new
                {
                    AgName = s.AgegroupName ?? "",
                    GDate = s.GDate!.Value,
                    Rnd = (int)(s.Rnd ?? 1)
                })
                .Distinct()
                .ToListAsync(ct);

            // Deduplicate: one entry per (AgName, GDate) — take max Rnd per date
            dateRows = scheduleRows
                .GroupBy(r => new { r.AgName, r.GDate })
                .Select(g => new { g.Key.AgName, g.Key.GDate, Rnd = g.Max(x => x.Rnd) })
                .ToList();
        }

        return dateRows
            .GroupBy(d => d.AgName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(d => new SourceDateEntry { GDate = d.GDate, Rnd = d.Rnd }).ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Dictionary<string, List<SourceFieldUsage>>> GetSourceFieldUsageAsync(
        Guid sourceJobId, CancellationToken ct = default)
    {
        // Extract field usage from actual game placements (RR games only)
        var raw = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == sourceJobId
                && s.GDate != null && s.FieldId != null && s.FName != null
                && s.T1Type == "T" && s.T2Type == "T")
            .Select(s => new
            {
                AgName = s.AgegroupName ?? "",
                FieldId = s.FieldId!.Value,
                FName = s.FName!,
                Dow = s.GDate!.Value.DayOfWeek
            })
            .ToListAsync(ct);

        return raw
            .GroupBy(r => r.AgName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(r => new { r.FieldId, r.FName })
                    .Select(fg => new SourceFieldUsage
                    {
                        FieldName = fg.Key.FName,
                        FieldId = fg.Key.FieldId,
                        GameCount = fg.Count(),
                        DaysUsed = fg.Select(r => r.Dow).Distinct().ToList()
                    })
                    .OrderByDescending(f => f.GameCount)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
    }
}
