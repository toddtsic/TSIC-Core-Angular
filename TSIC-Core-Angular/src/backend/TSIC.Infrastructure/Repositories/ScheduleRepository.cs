using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Constants;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Helpers;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Data.SqlDbContext.Helpers;
using TSIC.Infrastructure.Utilities;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for Schedule entity using Entity Framework Core.
/// </summary>
public sealed class ScheduleRepository : IScheduleRepository
{
    private readonly SqlDbContext _context;

    public ScheduleRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task SynchronizeScheduleNamesForTeamAsync(Guid teamId, Guid jobId, CancellationToken ct = default)
    {
        var team = await _context.Teams
            .AsNoTracking()
            .Where(t => t.TeamId == teamId)
            .Select(t => new { t.TeamName })
            .FirstOrDefaultAsync(ct);
        if (team == null) return;

        await ScheduleNameSyncHelper.ApplyTeamRenameToChangeTrackerAsync(
            _context, teamId, jobId, team.TeamName ?? string.Empty, ct);

        await _context.SaveChangesAsync(ct);
    }

    public async Task<int> SynchronizeScheduleDivisionForTeamAsync(
        Guid teamId, Guid jobId, Guid newAgegroupId, string newAgegroupName,
        Guid newDivId, string newDivName, CancellationToken ct = default)
    {
        var schedules = await _context.Schedule
            .Where(s => s.JobId == jobId
                && ((s.T1Id == teamId && s.T1Type == "T")
                 || (s.T2Id == teamId && s.T2Type == "T")))
            .ToListAsync(ct);

        foreach (var s in schedules)
        {
            // Update the game's grouping when this team is T1 (home)
            if (s.T1Id == teamId)
            {
                s.AgegroupId = newAgegroupId;
                s.AgegroupName = newAgegroupName;
                s.DivId = newDivId;
                s.DivName = newDivName;
            }
        }

        if (schedules.Count > 0)
            await _context.SaveChangesAsync(ct);

        return schedules.Count;
    }

    public async Task SynchronizeScheduleAgegroupNameAsync(Guid agegroupId, Guid jobId, string newName, CancellationToken ct = default)
    {
        var schedules = await _context.Schedule
            .Where(s => s.JobId == jobId && s.AgegroupId == agegroupId)
            .ToListAsync(ct);

        foreach (var s in schedules)
            s.AgegroupName = newName;

        if (schedules.Count > 0)
            await _context.SaveChangesAsync(ct);
    }

    public async Task SynchronizeScheduleDivisionNameAsync(Guid divId, Guid jobId, string newName, CancellationToken ct = default)
    {
        var schedules = await _context.Schedule
            .Where(s => s.JobId == jobId && (s.DivId == divId || s.Div2Id == divId))
            .ToListAsync(ct);

        foreach (var s in schedules)
        {
            if (s.DivId == divId) s.DivName = newName;
            if (s.Div2Id == divId) s.Div2Name = newName;
        }

        if (schedules.Count > 0)
            await _context.SaveChangesAsync(ct);
    }

    public async Task SynchronizeScheduleFieldNameAsync(Guid fieldId, string newName, CancellationToken ct = default)
    {
        await _context.Schedule
            .Where(s => s.FieldId == fieldId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.FName, newName), ct);
    }

    public async Task SynchronizeScheduleLeagueNameAsync(Guid leagueId, string newName, CancellationToken ct = default)
    {
        await _context.Schedule
            .Where(s => s.LeagueId == leagueId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.LeagueName, newName), ct);
    }

    public async Task SynchronizeAllScheduleNamesForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var showTeamNameOnly = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.BShowTeamNameOnlyInSchedules)
            .FirstOrDefaultAsync(ct);

        var teamRows = await (
            from t in _context.Teams.AsNoTracking()
            where t.JobId == jobId
            join r in _context.Registrations on t.ClubrepRegistrationid equals r.RegistrationId into rg
            from r in rg.DefaultIfEmpty()
            select new
            {
                t.TeamId,
                t.TeamName,
                ClubName = r != null ? r.ClubName : null
            }
        ).ToListAsync(ct);

        if (teamRows.Count == 0) return;

        var nameByTeamId = teamRows.ToDictionary(
            t => t.TeamId,
            t => (!string.IsNullOrEmpty(t.ClubName) && !showTeamNameOnly)
                ? $"{t.ClubName}:{t.TeamName}"
                : t.TeamName ?? string.Empty);

        var schedules = await _context.Schedule
            .Where(s => s.JobId == jobId
                && ((s.T1Id != null && s.T1Type == "T")
                 || (s.T2Id != null && s.T2Type == "T")))
            .ToListAsync(ct);

        foreach (var s in schedules)
        {
            if (s.T1Id.HasValue && s.T1Type == "T" && nameByTeamId.TryGetValue(s.T1Id.Value, out var t1Name))
                s.T1Name = t1Name;
            if (s.T2Id.HasValue && s.T2Type == "T" && nameByTeamId.TryGetValue(s.T2Id.Value, out var t2Name))
                s.T2Name = t2Name;
        }

        if (schedules.Count > 0)
            await _context.SaveChangesAsync(ct);
    }

    public async Task SynchronizeScheduleTeamAssignmentsForDivisionAsync(Guid divId, Guid jobId, CancellationToken ct = default)
    {
        // 1. Get active teams in the division with their DivRank and club-rep link
        var teams = await _context.Teams
            .AsNoTracking()
            .Where(t => t.DivId == divId && t.Active == true)
            .Select(t => new { t.TeamId, t.DivRank, t.TeamName, t.ClubrepRegistrationid })
            .ToListAsync(ct);

        // 2. Get club names for teams that have a club rep
        var clubRepIds = teams
            .Where(t => t.ClubrepRegistrationid.HasValue)
            .Select(t => t.ClubrepRegistrationid!.Value)
            .Distinct()
            .ToList();

        var clubNames = clubRepIds.Count > 0
            ? await _context.Registrations
                .AsNoTracking()
                .Where(r => clubRepIds.Contains(r.RegistrationId))
                .ToDictionaryAsync(r => r.RegistrationId, r => r.ClubName, ct)
            : new Dictionary<Guid, string?>();

        // 3. Get the job's BShowTeamNameOnlyInSchedules flag
        var showTeamNameOnly = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.BShowTeamNameOnlyInSchedules)
            .FirstOrDefaultAsync(ct);

        // 4. Build rank → (teamId, displayName) map
        var rankMap = new Dictionary<int, (Guid teamId, string displayName)>();
        foreach (var t in teams)
        {
            string? clubName = t.ClubrepRegistrationid.HasValue
                && clubNames.TryGetValue(t.ClubrepRegistrationid.Value, out var cn)
                ? cn : null;

            var displayName = (!string.IsNullOrEmpty(clubName) && !showTeamNameOnly)
                ? $"{clubName}:{t.TeamName}"
                : t.TeamName ?? "";

            rankMap[t.DivRank] = (t.TeamId, displayName);
        }

        // 5. Load all round-robin schedule records for this division
        var schedules = await _context.Schedule
            .Where(s => s.JobId == jobId
                && (s.DivId == divId || s.Div2Id == divId))
            .ToListAsync(ct);

        // 6. Re-resolve T1Id/T1Name and T2Id/T2Name from T1No/T2No
        foreach (var s in schedules)
        {
            if (s.T1Type == "T" && s.T1No.HasValue && s.DivId == divId)
            {
                if (rankMap.TryGetValue(s.T1No.Value, out var t1))
                {
                    s.T1Id = t1.teamId;
                    s.T1Name = t1.displayName;
                }
                else
                {
                    s.T1Id = null;
                    s.T1Name = "";
                }
            }

            if (s.T2Type == "T" && s.T2No.HasValue)
            {
                // T2 uses Div2Id for cross-division games, DivId for same-division
                var t2DivId = s.Div2Id ?? s.DivId;
                if (t2DivId == divId)
                {
                    if (rankMap.TryGetValue(s.T2No.Value, out var t2))
                    {
                        s.T2Id = t2.teamId;
                        s.T2Name = t2.displayName;
                    }
                    else
                    {
                        s.T2Id = null;
                        s.T2Name = "";
                    }
                }
            }
        }

        if (schedules.Count > 0)
            await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Bracket-slot sibling of <see cref="SynchronizeScheduleTeamAssignmentsForDivisionAsync"/>.
    /// A "T" slot resolves its occupant from Teams.DivRank; a bracket slot has no occupant until
    /// seed resolution fills it, and until then its label is the director's seed intent, which
    /// lives in BracketSeeds. Restamps that label on every UNOCCUPIED bracket slot in
    /// <paramref name="gids"/>; slots with no intent (the fed targets of AdvancementFeeds) are
    /// left null and render as "{type}{no}". Occupied slots keep their resolved team name.
    /// </summary>
    public async Task<int> SynchronizeBracketSeedAnnotationsAsync(
        IReadOnlyCollection<int> gids, CancellationToken ct = default)
    {
        if (gids.Count == 0) return 0;

        var games = await _context.Schedule
            .Where(s => gids.Contains(s.Gid) && s.T1Type != null && s.T1Type != "T")
            .ToListAsync(ct);
        if (games.Count == 0) return 0;

        var intents = await _context.BracketSeeds
            .AsNoTracking()
            .Where(bs => gids.Contains(bs.Gid))
            .ToDictionaryAsync(bs => bs.Gid, ct);

        var seedDivIds = intents.Values
            .SelectMany(bs => new[] { bs.T1SeedDivId, bs.T2SeedDivId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var divNames = seedDivIds.Count > 0
            ? await _context.Divisions
                .AsNoTracking()
                .Where(d => seedDivIds.Contains(d.DivId))
                .ToDictionaryAsync(d => d.DivId, d => d.DivName, ct)
            : [];

        string? Label(Guid? seedDivId, int? seedRank, string? slotType, int? slotNo)
        {
            if (seedDivId is null || seedRank is null) return null;
            if (!divNames.TryGetValue(seedDivId.Value, out var divName)
                || string.IsNullOrEmpty(divName)) return null;
            return BracketSlotLabel.Format(slotType, slotNo, divName, seedRank.Value);
        }

        var stamped = 0;
        foreach (var s in games)
        {
            intents.TryGetValue(s.Gid, out var intent);

            if (s.T1Id is null)
            {
                var label = Label(intent?.T1SeedDivId, intent?.T1SeedRank, s.T1Type, s.T1No);
                if (s.T1Name != label) { s.T1Name = label; stamped++; }
            }

            if (s.T2Id is null)
            {
                var label = Label(intent?.T2SeedDivId, intent?.T2SeedRank, s.T2Type, s.T2No);
                if (s.T2Name != label) { s.T2Name = label; stamped++; }
            }
        }

        if (stamped > 0) await _context.SaveChangesAsync(ct);
        return stamped;
    }

    // ── Schedule Division (009-4) ──

    public async Task<List<Domain.Entities.Schedule>> GetGamesForGridAsync(
        Guid jobId, List<Guid> fieldIds, List<DateTime> gameDates, CancellationToken ct = default)
    {
        return await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId
                && s.FieldId.HasValue && fieldIds.Contains(s.FieldId.Value)
                && s.GDate.HasValue && gameDates.Contains(s.GDate.Value))
            .OrderBy(s => s.GDate)
            .ToListAsync(ct);
    }

    public async Task<List<Domain.Entities.Schedule>> GetGamesForGridByDateRangeAsync(
        Guid jobId, List<Guid> fieldIds, DateTime dateFrom, DateTime dateTo, CancellationToken ct = default)
    {
        return await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId
                && s.FieldId.HasValue && fieldIds.Contains(s.FieldId.Value)
                && s.GDate.HasValue && s.GDate.Value >= dateFrom && s.GDate.Value < dateTo)
            .OrderBy(s => s.GDate)
            .ToListAsync(ct);
    }

    public async Task<HashSet<(Guid fieldId, DateTime gDate)>> GetOccupiedSlotsAsync(
        Guid jobId, List<Guid> fieldIds, CancellationToken ct = default)
    {
        var slots = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId
                && s.FieldId.HasValue && fieldIds.Contains(s.FieldId.Value)
                && s.GDate.HasValue)
            .Select(s => new { FieldId = s.FieldId!.Value, GDate = s.GDate!.Value })
            .ToListAsync(ct);

        return slots.Select(s => (s.FieldId, s.GDate)).ToHashSet();
    }

    public async Task<Domain.Entities.Schedule?> GetGameByIdAsync(int gid, CancellationToken ct = default)
    {
        return await _context.Schedule.FindAsync(new object[] { gid }, ct);
    }

    public async Task<GamePushKeysDto?> GetGamePushKeysAsync(int gid, CancellationToken ct = default)
    {
        return await _context.Schedule
            .AsNoTracking()
            .Where(s => s.Gid == gid)
            .Select(s => new GamePushKeysDto
            {
                JobId = s.JobId,
                T1Id = s.T1Id,
                T2Id = s.T2Id,
                T1Name = s.T1Name,
                T2Name = s.T2Name,
                T1Score = s.T1Score,
                T2Score = s.T2Score,
                AgegroupName = s.Agegroup != null ? s.Agegroup.AgegroupName : null,
                DivName = s.Div != null ? s.Div.DivName : null
            })
            .SingleOrDefaultAsync(ct);
    }

    public async Task<List<Domain.Entities.Schedule>> GetGamesByIdsAsync(List<int> gids, CancellationToken ct = default)
    {
        return await _context.Schedule
            .Where(s => gids.Contains(s.Gid))
            .ToListAsync(ct);
    }

    public async Task<List<Domain.Entities.Schedule>> GetDivisionGamesTrackedAsync(
        Guid jobId, Guid agegroupId, Guid divId, CancellationToken ct = default)
    {
        return await _context.Schedule
            .Where(s => s.JobId == jobId && s.AgegroupId == agegroupId && s.DivId == divId)
            .ToListAsync(ct);
    }

    public async Task<List<Domain.Entities.Schedule>> GetAgegroupGamesTrackedAsync(
        Guid jobId, Guid agegroupId, CancellationToken ct = default)
    {
        return await _context.Schedule
            .Where(s => s.JobId == jobId && s.AgegroupId == agegroupId)
            .ToListAsync(ct);
    }

    public async Task<List<Domain.Entities.Schedule>> GetJobGamesTrackedAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _context.Schedule
            .Where(s => s.JobId == jobId)
            .ToListAsync(ct);
    }

    public async Task<Domain.Entities.Schedule?> GetGameAtSlotAsync(DateTime gDate, Guid fieldId, CancellationToken ct = default)
    {
        return await _context.Schedule
            .FirstOrDefaultAsync(s => s.GDate == gDate && s.FieldId == fieldId, ct);
    }

    public void AddGame(Domain.Entities.Schedule game)
    {
        _context.Schedule.Add(game);
    }

    public async Task DeleteGameAsync(int gid, CancellationToken ct = default)
    {
        // Cascade order: DeviceGids → BracketSeeds → Schedule
        var deviceGids = await _context.DeviceGids
            .Where(dg => dg.Gid == gid)
            .ToListAsync(ct);
        if (deviceGids.Count > 0)
            _context.DeviceGids.RemoveRange(deviceGids);

        var bracketSeeds = await _context.BracketSeeds
            .Where(bs => bs.Gid == gid)
            .ToListAsync(ct);
        if (bracketSeeds.Count > 0)
            _context.BracketSeeds.RemoveRange(bracketSeeds);

        await StageBracketMetadataCleanupAsync(new List<int> { gid }, ct);

        var game = await _context.Schedule.FindAsync(new object[] { gid }, ct);
        if (game != null)
            _context.Schedule.Remove(game);
    }

    // brackets.* references (FK, NO ACTION) must be cleared before the schedule
    // rows they point at, or the delete fails. A game can be an advancement feed
    // source or target, and can carry a seed assignment.
    private async Task StageBracketMetadataCleanupAsync(List<int> gids, CancellationToken ct)
    {
        var feeds = await _context.AdvancementFeeds
            .Where(f => gids.Contains(f.SourceGid) || gids.Contains(f.TargetGid))
            .ToListAsync(ct);
        if (feeds.Count > 0) _context.AdvancementFeeds.RemoveRange(feeds);

        var seeds = await _context.SeedAssignments
            .Where(s => gids.Contains(s.Gid))
            .ToListAsync(ct);
        if (seeds.Count > 0) _context.SeedAssignments.RemoveRange(seeds);
    }

    public async Task DeleteDivisionGamesAsync(Guid divId, Guid leagueId, string season, string year, CancellationToken ct = default)
    {
        // Find all game Gids for this division
        var gids = await _context.Schedule
            .Where(s => (s.DivId == divId || s.Div2Id == divId)
                && s.LeagueId == leagueId
                && s.Season == season
                && s.Year == year)
            .Select(s => s.Gid)
            .ToListAsync(ct);

        if (gids.Count == 0) return;

        // Cascade order: DeviceGids → BracketSeeds → Schedule
        var deviceGids = await _context.DeviceGids
            .Where(dg => gids.Contains(dg.Gid))
            .ToListAsync(ct);
        if (deviceGids.Count > 0)
            _context.DeviceGids.RemoveRange(deviceGids);

        var bracketSeeds = await _context.BracketSeeds
            .Where(bs => gids.Contains(bs.Gid))
            .ToListAsync(ct);
        if (bracketSeeds.Count > 0)
            _context.BracketSeeds.RemoveRange(bracketSeeds);

        await StageBracketMetadataCleanupAsync(gids, ct);

        var games = await _context.Schedule
            .Where(s => gids.Contains(s.Gid))
            .ToListAsync(ct);
        _context.Schedule.RemoveRange(games);
    }

    // ── Cascade date operations ──

    public async Task<int> UpdateGameDatesAsync(
        Guid jobId, DateTime oldDate, DateTime newDate, CancellationToken ct = default)
    {
        var games = await _context.Schedule
            .Where(s => s.JobId == jobId && s.GDate.HasValue && s.GDate.Value.Date == oldDate.Date)
            .ToListAsync(ct);

        foreach (var g in games)
            g.GDate = newDate.Date + g.GDate!.Value.TimeOfDay;

        return games.Count;
    }

    public async Task<int> DeleteGamesByDateAsync(
        Guid jobId, DateTime date, CancellationToken ct = default)
    {
        var gids = await _context.Schedule
            .Where(s => s.JobId == jobId && s.GDate.HasValue && s.GDate.Value.Date == date.Date)
            .Select(s => s.Gid)
            .ToListAsync(ct);

        if (gids.Count == 0) return 0;

        // Cascade order: DeviceGids → BracketSeeds → Schedule
        var deviceGids = await _context.DeviceGids
            .Where(dg => gids.Contains(dg.Gid))
            .ToListAsync(ct);
        if (deviceGids.Count > 0)
            _context.DeviceGids.RemoveRange(deviceGids);

        var bracketSeeds = await _context.BracketSeeds
            .Where(bs => gids.Contains(bs.Gid))
            .ToListAsync(ct);
        if (bracketSeeds.Count > 0)
            _context.BracketSeeds.RemoveRange(bracketSeeds);

        await StageBracketMetadataCleanupAsync(gids, ct);

        var games = await _context.Schedule
            .Where(s => gids.Contains(s.Gid))
            .ToListAsync(ct);
        _context.Schedule.RemoveRange(games);

        return gids.Count;
    }

    public async Task<int> DeleteDivisionGamesByDateAsync(
        Guid divId, Guid leagueId, string season, string year, DateTime date, CancellationToken ct = default)
    {
        var gids = await _context.Schedule
            .Where(s => (s.DivId == divId || s.Div2Id == divId)
                && s.LeagueId == leagueId
                && s.Season == season
                && s.Year == year
                && s.GDate.HasValue && s.GDate.Value.Date == date.Date)
            .Select(s => s.Gid)
            .ToListAsync(ct);

        if (gids.Count == 0) return 0;

        // Cascade order: DeviceGids → BracketSeeds → Schedule
        var deviceGids = await _context.DeviceGids
            .Where(dg => gids.Contains(dg.Gid))
            .ToListAsync(ct);
        if (deviceGids.Count > 0)
            _context.DeviceGids.RemoveRange(deviceGids);

        var bracketSeeds = await _context.BracketSeeds
            .Where(bs => gids.Contains(bs.Gid))
            .ToListAsync(ct);
        if (bracketSeeds.Count > 0)
            _context.BracketSeeds.RemoveRange(bracketSeeds);

        await StageBracketMetadataCleanupAsync(gids, ct);

        var games = await _context.Schedule
            .Where(s => gids.Contains(s.Gid))
            .ToListAsync(ct);
        _context.Schedule.RemoveRange(games);

        return gids.Count;
    }

    public async Task<List<GameDateInfoDto>> GetDistinctGameDatesAsync(
        Guid jobId, Guid? agegroupId = null, Guid? divId = null, CancellationToken ct = default)
    {
        var query = _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate.HasValue);

        if (divId.HasValue)
            query = query.Where(s => s.DivId == divId.Value || s.Div2Id == divId.Value);
        else if (agegroupId.HasValue)
            query = query.Where(s => s.AgegroupId == agegroupId.Value);

        return await query
            .GroupBy(s => s.GDate!.Value.Date)
            .Select(g => new GameDateInfoDto
            {
                Date = g.Key,
                GameCount = g.Count()
            })
            .OrderBy(d => d.Date)
            .ToListAsync(ct);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }

    // ── View Schedule (009-5) ──

    public async Task<List<Domain.Entities.Schedule>> GetFilteredGamesAsync(
        Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default)
    {
        var query = _context.Schedule
            .AsNoTracking()
            .Include(s => s.Field)
            .Include(s => s.Agegroup)
            .Include(s => s.GStatusCodeNavigation)
            .Include(s => s.T1TypeNavigation)
            .Include(s => s.T2TypeNavigation)
            .Where(s => s.JobId == jobId && s.GDate.HasValue);

        query = ApplyCadtFilter(query, request);

        if (request.GameDays is { Count: > 0 })
        {
            var days = request.GameDays.Select(d => d.Date).ToList();
            query = query.Where(s => s.GDate.HasValue && days.Contains(s.GDate.Value.Date));
        }

        if (request.FieldIds is { Count: > 0 })
            query = query.Where(s => s.FieldId.HasValue && request.FieldIds.Contains(s.FieldId.Value));

        if (request.Times is { Count: > 0 })
        {
            // Convert "HH:mm" strings to minutes-since-midnight for EF-translatable IN clause
            var minutesList = request.Times.Select(t =>
            {
                var parts = t.Split(':');
                return int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
            }).ToList();
            query = query.Where(s => s.GDate.HasValue
                && minutesList.Contains(s.GDate!.Value.Hour * 60 + s.GDate!.Value.Minute));
        }

        if (request.UnscoredOnly == true)
            query = query.Where(s => s.T1Score == null && s.T2Score == null);

        // Total order: GDate then FName. Without a tiebreaker, same-kickoff rows (many fields
        // at one time) come back in unstable plan order — toggling UnscoredOnly reshuffles them.
        return await query.OrderBy(s => s.GDate).ThenBy(s => s.FName).ToListAsync(ct);
    }

    public async Task<ScheduleFilterOptionsDto> GetScheduleFilterOptionsAsync(
        Guid jobId, CancellationToken ct = default)
    {
        // 1. Get all distinct team IDs from the schedule
        var baseQuery = _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate.HasValue);
        var t1 = baseQuery.Where(s => s.T1Id.HasValue).Select(s => s.T1Id!.Value);
        var t2 = baseQuery.Where(s => s.T2Id.HasValue).Select(s => s.T2Id!.Value);
        var scheduledTeamIds = await t1.Union(t2).Distinct().ToListAsync(ct);

        if (scheduledTeamIds.Count == 0)
        {
            return new ScheduleFilterOptionsDto
            {
                Clubs = [],
                Agegroups = [],
                GameDays = [],
                Times = [],
                Fields = []
            };
        }

        // 2. Explicit joins — project flat rows with full CADT path
        var teamRows = await (
            from t in _context.Teams.AsNoTracking()
            where scheduledTeamIds.Contains(t.TeamId) && t.Active == true
            join ag in _context.Agegroups.AsNoTracking()
                on t.AgegroupId equals ag.AgegroupId
            join div in _context.Divisions.AsNoTracking()
                on t.DivId equals (Guid?)div.DivId into divJoin
            from div in divJoin.DefaultIfEmpty()
            join reg in _context.Registrations.AsNoTracking()
                on t.ClubrepRegistrationid equals (Guid?)reg.RegistrationId into regJoin
            from reg in regJoin.DefaultIfEmpty()
            select new
            {
                t.TeamId,
                t.TeamName,
                t.AgegroupId,
                AgegroupName = ag.AgegroupName,
                AgegroupColor = ag.Color,
                DivId = div != null ? (Guid?)div.DivId : null,
                DivName = div != null ? div.DivName : null,
                ClubName = reg != null ? reg.ClubName : null
            }
        ).ToListAsync(ct);

        // 3. If no teams have club associations, hide CADT filter entirely
        var hasClubs = teamRows.Any(t => t.ClubName != null);
        var cadtTree = !hasClubs
            ? new List<CadtClubNode>()
            : teamRows
                .Select(t => new
                {
                    ClubName = t.ClubName ?? "Unaffiliated",
                    t.AgegroupId,
                    t.AgegroupName,
                    t.AgegroupColor,
                    t.DivId,
                    t.DivName,
                    t.TeamId,
                    t.TeamName
                })
                .GroupBy(t => t.ClubName)
                .OrderBy(c => c.Key)
                .Select(clubGroup => new CadtClubNode
                {
                    ClubName = clubGroup.Key,
                    Agegroups = clubGroup
                        .GroupBy(t => new { t.AgegroupId, t.AgegroupName, t.AgegroupColor })
                        .OrderBy(a => a.Key.AgegroupName)
                        .Select(agGroup => new CadtAgegroupNode
                        {
                            AgegroupId = agGroup.Key.AgegroupId,
                            AgegroupName = agGroup.Key.AgegroupName ?? "",
                            Color = agGroup.Key.AgegroupColor,
                            Divisions = agGroup
                                .Where(t => t.DivId.HasValue)
                                .GroupBy(t => new { DivId = t.DivId!.Value, t.DivName })
                                .OrderBy(d => d.Key.DivName)
                                .Select(divGroup => new CadtDivisionNode
                                {
                                    DivId = divGroup.Key.DivId,
                                    DivName = divGroup.Key.DivName ?? "",
                                    Teams = divGroup
                                        .OrderBy(t => t.TeamName)
                                        .Select(t => new CadtTeamNode
                                        {
                                            TeamId = t.TeamId,
                                            TeamName = t.TeamName ?? ""
                                        })
                                        .ToList()
                                })
                                .ToList()
                        })
                        .ToList()
                })
                .ToList();

        // 5. Get distinct game days
        var gameDays = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate.HasValue)
            .Select(s => s.GDate!.Value.Date)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync(ct);

        // 6. Get distinct fields used in games. Materialize the field rows, then compose the
        //    display address in memory — FieldAddressFormatter is a C# helper EF can't translate.
        var fieldRows = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.FieldId.HasValue && s.GDate.HasValue)
            .Select(s => s.FieldId!.Value)
            .Distinct()
            .Join(_context.Fields.AsNoTracking(),
                fid => fid,
                f => f.FieldId,
                (fid, f) => f)
            .ToListAsync(ct);

        var fields = fieldRows
            .Select(f => new FieldSummaryDto
            {
                FieldId = f.FieldId,
                FName = f.FName ?? "",
                FAddress = FieldAddressFormatter.Build(f)
            })
            .OrderBy(f => f.FName)
            .ToList();

        // 7. Build LADT tree (Agegroup → Division → Team) from same data, ignoring club
        var ladtTree = teamRows
            .GroupBy(t => new { t.AgegroupId, t.AgegroupName, t.AgegroupColor })
            .OrderBy(a => a.Key.AgegroupName)
            .Select(agGroup => new LadtAgegroupNode
            {
                AgegroupId = agGroup.Key.AgegroupId,
                AgegroupName = agGroup.Key.AgegroupName ?? "",
                Color = agGroup.Key.AgegroupColor,
                Divisions = agGroup
                    .Where(t => t.DivId.HasValue)
                    .GroupBy(t => new { DivId = t.DivId!.Value, t.DivName })
                    .OrderBy(d => d.Key.DivName)
                    .Select(divGroup => new LadtDivisionNode
                    {
                        DivId = divGroup.Key.DivId,
                        DivName = divGroup.Key.DivName ?? "",
                        Teams = divGroup
                            .Select(t => new { t.TeamId, t.TeamName })
                            .Distinct()
                            .OrderBy(t => t.TeamName)
                            .Select(t => new LadtTeamNode
                            {
                                TeamId = t.TeamId,
                                TeamName = t.TeamName ?? ""
                            })
                            .ToList()
                    })
                    .ToList()
            })
            .ToList();

        // 8. Extract distinct game times as "HH:mm" strings
        var times = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate.HasValue)
            .Select(s => s.GDate!.Value.Hour * 60 + s.GDate!.Value.Minute)
            .Distinct()
            .OrderBy(m => m)
            .ToListAsync(ct);

        var timeStrings = times
            .Select(m => $"{m / 60:D2}:{m % 60:D2}")
            .ToList();

        // 9. Does this job have any bracket games? Uses GameRoundTypes.Bracket (the ladder
        //    rounds PLUS bronze) — a round-robin-only job has none, so the Brackets tab hides.
        var bracketTypes = GameRoundTypes.Bracket;
        var jobHasBrackets = await _context.Schedule
            .AsNoTracking()
            .AnyAsync(s => s.JobId == jobId
                && (bracketTypes.Contains(s.T1Type) || bracketTypes.Contains(s.T2Type)), ct);

        return new ScheduleFilterOptionsDto
        {
            Clubs = cadtTree,
            Agegroups = ladtTree,
            GameDays = gameDays,
            Times = timeStrings,
            Fields = fields,
            JobHasBrackets = jobHasBrackets
        };
    }

    public async Task<List<Domain.Entities.Schedule>> GetTeamGamesAsync(
        Guid teamId, CancellationToken ct = default)
    {
        return await _context.Schedule
            .AsNoTracking()
            .Include(s => s.Field)
            .Include(s => s.GStatusCodeNavigation)
            .Include(s => s.T1TypeNavigation)
            .Include(s => s.T2TypeNavigation)
            .Where(s => (s.T1Id == teamId || s.T2Id == teamId) && s.GDate.HasValue)
            .OrderBy(s => s.GDate)
            .ThenBy(s => s.FName)
            .ToListAsync(ct);
    }

    public async Task<List<GameStatusOptionDto>> GetGameStatusOptionsAsync(CancellationToken ct = default)
    {
        return await _context.GameStatusCodes
            .AsNoTracking()
            .OrderBy(s => s.GStatusCode)
            .Select(s => new GameStatusOptionDto
            {
                Code = s.GStatusCode,
                Text = s.GStatusText ?? ""
            })
            .ToListAsync(ct);
    }

    public async Task<List<Domain.Entities.Schedule>> GetBracketGamesAsync(
        Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default)
    {
        var bracketTypes = GameRoundTypes.Bracket;
        var query = _context.Schedule
            .AsNoTracking()
            .Include(s => s.Field)
            .Where(s => s.JobId == jobId
                && (bracketTypes.Contains(s.T1Type) || bracketTypes.Contains(s.T2Type)));

        // Apply agegroup/division filters if specified
        if (request.AgegroupIds is { Count: > 0 })
            query = query.Where(s => s.AgegroupId.HasValue && request.AgegroupIds.Contains(s.AgegroupId.Value));

        if (request.DivisionIds is { Count: > 0 })
            query = query.Where(s => s.DivId.HasValue && request.DivisionIds.Contains(s.DivId.Value));

        return await query.OrderBy(s => s.GDate).ThenBy(s => s.FName).ToListAsync(ct);
    }

    public async Task<List<Domain.Entities.Schedule>> GetConsolationGamesAsync(
        Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default)
    {
        var query = _context.Schedule
            .AsNoTracking()
            .Include(s => s.Field)
            .Where(s => s.JobId == jobId
                && s.T1Type == GameRoundTypes.Consolation
                && s.T2Type == GameRoundTypes.Consolation);

        if (request.AgegroupIds is { Count: > 0 })
            query = query.Where(s => s.AgegroupId.HasValue && request.AgegroupIds.Contains(s.AgegroupId.Value));

        if (request.DivisionIds is { Count: > 0 })
            query = query.Where(s => s.DivId.HasValue && request.DivisionIds.Contains(s.DivId.Value));

        return await query.OrderBy(s => s.GDate).ThenBy(s => s.FName).ToListAsync(ct);
    }

    public async Task<List<ContactDto>> GetContactsAsync(
        Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default)
    {
        // 1. Get team IDs from the filtered schedule
        var gameQuery = _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate.HasValue);

        gameQuery = ApplyCadtFilter(gameQuery, request);

        // EF Core can't translate SelectMany with array initializer — use Union instead
        var t1Ids = gameQuery.Where(s => s.T1Id.HasValue).Select(s => s.T1Id!.Value);
        var t2Ids = gameQuery.Where(s => s.T2Id.HasValue).Select(s => s.T2Id!.Value);
        var teamIds = await t1Ids.Union(t2Ids).Distinct().ToListAsync(ct);

        if (teamIds.Count == 0) return [];

        // 2. Get staff registrations assigned to these teams, with team/agegroup/division navigations
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId
                && r.BActive == true
                && r.RoleId == RoleConstants.Staff
                && r.AssignedTeamId.HasValue
                && teamIds.Contains(r.AssignedTeamId.Value))
            .Select(r => new ContactDto
            {
                AgegroupName = r.AssignedTeam != null ? r.AssignedTeam.Agegroup.AgegroupName ?? "" : "",
                DivName = r.AssignedTeam != null && r.AssignedTeam.Div != null ? r.AssignedTeam.Div.DivName ?? "" : "",
                ClubName = r.ClubName ?? "",
                TeamName = r.AssignedTeam != null ? r.AssignedTeam.TeamName ?? "" : "",
                FirstName = r.User != null ? r.User.FirstName ?? "" : "",
                LastName = r.User != null ? r.User.LastName ?? "" : "",
                Cellphone = r.User != null ? r.User.Cellphone : null,
                Email = r.User != null ? r.User.Email : null
            })
            .OrderBy(c => c.AgegroupName)
            .ThenBy(c => c.DivName)
            .ThenBy(c => c.ClubName)
            .ThenBy(c => c.TeamName)
            .ThenBy(c => c.LastName)
            .ToListAsync(ct);
    }

    public async Task<FieldDisplayDto?> GetFieldDisplayAsync(Guid fieldId, CancellationToken ct = default)
    {
        return await _context.Fields
            .AsNoTracking()
            .Where(f => f.FieldId == fieldId)
            .Select(f => new FieldDisplayDto
            {
                FieldId = f.FieldId,
                FName = f.FName ?? "",
                Address = f.Address,
                City = f.City,
                State = f.State,
                Zip = f.Zip,
                Directions = f.Directions,
                Latitude = f.Latitude,
                Longitude = f.Longitude
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<string> GetSportNameAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.Sport.SportName ?? "Soccer")
            .FirstOrDefaultAsync(ct) ?? "Soccer";
    }

    public async Task<(bool allowPublicAccess, bool hideContacts, string sportName)> GetScheduleFlagsAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var jobFlags = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new
            {
                AllowPublic = j.BScheduleAllowPublicAccess ?? false,
                SportName = j.Sport.SportName ?? "Soccer"
            })
            .FirstOrDefaultAsync(ct);

        // Get BHideContacts: prefer the primary league, fall back to the only/first
        // league when no row is marked primary (mirrors JobLeagueRepository.GetPrimaryLeagueForJobAsync).
        var hideContacts = await _context.JobLeagues
            .AsNoTracking()
            .Where(jl => jl.JobId == jobId)
            .OrderByDescending(jl => jl.BIsPrimary)
            .Select(jl => jl.League.BHideContacts)
            .FirstOrDefaultAsync(ct);

        return (
            jobFlags?.AllowPublic ?? false,
            hideContacts,
            jobFlags?.SportName ?? "Soccer"
        );
    }

    public async Task<HashSet<Guid>> GetChampionsByDivisionAgegroupIdsAsync(
        IEnumerable<Guid> agegroupIds, CancellationToken ct = default)
    {
        var ids = agegroupIds.ToList();
        if (ids.Count == 0) return new HashSet<Guid>();

        var result = await _context.Agegroups
            .AsNoTracking()
            .Where(a => ids.Contains(a.AgegroupId) && a.BChampionsByDivision == true)
            .Select(a => a.AgegroupId)
            .ToListAsync(ct);

        return new HashSet<Guid>(result);
    }

    // ── Rescheduler (009-6) ──

    public async Task<ScheduleGridResponse> GetReschedulerGridAsync(
        Guid jobId, ReschedulerGridRequest request, CancellationToken ct = default)
    {
        // 1. Query games matching filters
        var query = _context.Schedule
            .AsNoTracking()
            .Include(s => s.Agegroup)
            .Where(s => s.JobId == jobId && s.GDate.HasValue && s.FieldId.HasValue);

        // Apply CADT filter using a ScheduleFilterRequest (same pattern as View Schedule)
        var cadtFilter = new ScheduleFilterRequest
        {
            ClubNames = request.ClubNames,
            AgegroupIds = request.AgegroupIds,
            DivisionIds = request.DivisionIds,
            TeamIds = request.TeamIds
        };
        query = ApplyCadtFilter(query, cadtFilter);

        // Apply GameDays filter
        if (request.GameDays is { Count: > 0 })
        {
            var days = request.GameDays.Select(d => d.Date).ToList();
            query = query.Where(s => s.GDate.HasValue && days.Contains(s.GDate.Value.Date));
        }

        // Apply FieldIds filter
        if (request.FieldIds is { Count: > 0 })
            query = query.Where(s => s.FieldId.HasValue && request.FieldIds.Contains(s.FieldId.Value));

        var games = await query.OrderBy(s => s.GDate).ToListAsync(ct);

        // 2. Build distinct field columns from matched games (+ any FieldIds from the filter)
        var fieldIdSet = games
            .Where(g => g.FieldId.HasValue)
            .Select(g => g.FieldId!.Value)
            .ToHashSet();

        if (request.FieldIds is { Count: > 0 })
        {
            foreach (var fid in request.FieldIds)
                fieldIdSet.Add(fid);
        }

        var columns = fieldIdSet.Count > 0
            ? await _context.Fields
                .AsNoTracking()
                .Where(f => fieldIdSet.Contains(f.FieldId))
                .OrderBy(f => f.FName)
                .Select(f => new ScheduleFieldColumn { FieldId = f.FieldId, FName = f.FName ?? "" })
                .ToListAsync(ct)
            : new List<ScheduleFieldColumn>();

        // 3. Build distinct timeslot rows from game GDates
        var timeslots = new SortedSet<DateTime>(
            games
                .Where(g => g.GDate.HasValue)
                .Select(g => g.GDate!.Value)
                .Distinct());

        // Inject additional timeslot if provided
        if (request.AdditionalTimeslot.HasValue)
            timeslots.Add(request.AdditionalTimeslot.Value);

        // 4. Index games by (GDate, FieldId) for O(1) cell lookup
        var gameIndex = new Dictionary<(DateTime, Guid), Domain.Entities.Schedule>();
        foreach (var game in games)
        {
            if (game.GDate.HasValue && game.FieldId.HasValue)
                gameIndex[(game.GDate.Value, game.FieldId.Value)] = game;
        }

        // 5. Assemble grid rows
        var rows = new List<ScheduleGridRow>();
        foreach (var timeslot in timeslots)
        {
            var cells = columns
                .Select(col => gameIndex.TryGetValue((timeslot, col.FieldId), out var game)
                    ? Utilities.ScheduleGameDtoMapper.Map(game)
                    : null)
                .ToList();

            rows.Add(new ScheduleGridRow { GDate = timeslot, Cells = cells });
        }

        return new ScheduleGridResponse { Columns = columns, Rows = rows };
    }

    public async Task<int> GetAffectedGameCountAsync(
        Guid jobId, DateTime preFirstGame, List<Guid> fieldIds, CancellationToken ct = default)
    {
        var day = preFirstGame.Date;
        var query = _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate.HasValue && s.GDate.Value.Date == day);

        if (fieldIds.Count > 0)
            query = query.Where(s => s.FieldId.HasValue && fieldIds.Contains(s.FieldId.Value));

        return await query.CountAsync(ct);
    }

    public async Task<List<ScheduleEmailRecipient>> GetEmailRecipientsAsync(
        Guid jobId, DateTime firstGame, DateTime lastGame, List<Guid> fieldIds, CancellationToken ct = default)
    {
        // 1. Find games in date/field range
        var gameQuery = _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate.HasValue
                && s.GDate.Value >= firstGame && s.GDate.Value <= lastGame);

        if (fieldIds.Count > 0)
            gameQuery = gameQuery.Where(s => s.FieldId.HasValue && fieldIds.Contains(s.FieldId.Value));

        // 2. Get distinct team IDs from matched games
        // EF Core can't translate SelectMany with array initializer — use Union instead
        var emailT1 = gameQuery.Where(s => s.T1Id.HasValue).Select(s => s.T1Id!.Value);
        var emailT2 = gameQuery.Where(s => s.T2Id.HasValue).Select(s => s.T2Id!.Value);
        var teamIds = await emailT1.Union(emailT2).Distinct().ToListAsync(ct);

        if (teamIds.Count == 0) return new List<ScheduleEmailRecipient>();

        // De-dup by address (first occurrence wins). Each address carries the registration it
        // belongs to + that reg's opt-out flag, so the batch engine can append the per-reg
        // unsubscribe footer and suppress unsubscribers. League addon contacts have no
        // registration (regId = null → no footer, not suppressible — operational notice).
        // One address rule, shared with the batch engine — see EmailAddressRules. This used to run
        // EmailAddressAttribute, which only asserts a single non-terminal '@' and so happily passed
        // `foo@gmail` straight to SES.
        var byEmail = new Dictionary<string, ScheduleEmailRecipient>(StringComparer.OrdinalIgnoreCase);
        void Add(Guid? regId, string? email, bool optedOut)
        {
            if (!EmailAddressRules.IsSendable(email)) return;
            var trimmed = email!.Trim();
            if (!byEmail.ContainsKey(trimmed))
                byEmail[trimmed] = new ScheduleEmailRecipient { RegistrationId = regId, Email = trimmed, OptedOut = optedOut };
        }

        // 3. Player + parent emails — carry the player's registration + opt-out
        var rosterEmails = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.AssignedTeamId.HasValue && teamIds.Contains(r.AssignedTeamId.Value))
            .Select(r => new
            {
                r.RegistrationId,
                r.BemailOptOut,
                PlayerEmail = r.User != null ? r.User.Email : null,
                MomEmail = r.FamilyUser != null ? r.FamilyUser.MomEmail : null,
                DadEmail = r.FamilyUser != null ? r.FamilyUser.DadEmail : null
            })
            .ToListAsync(ct);

        foreach (var e in rosterEmails)
        {
            Add(e.RegistrationId, e.PlayerEmail, e.BemailOptOut);
            Add(e.RegistrationId, e.MomEmail, e.BemailOptOut);
            Add(e.RegistrationId, e.DadEmail, e.BemailOptOut);
        }

        // 4. Club rep emails — carry the club-rep's registration + opt-out
        var clubReps = await _context.Teams
            .AsNoTracking()
            .Where(t => teamIds.Contains(t.TeamId)
                && t.ClubrepRegistration != null
                && t.ClubrepRegistration.User != null
                && !string.IsNullOrEmpty(t.ClubrepRegistration.User.Email))
            .Select(t => new
            {
                t.ClubrepRegistration!.RegistrationId,
                t.ClubrepRegistration.BemailOptOut,
                Email = t.ClubrepRegistration.User!.Email!
            })
            .Distinct()
            .ToListAsync(ct);

        foreach (var rep in clubReps)
            Add(rep.RegistrationId, rep.Email, rep.BemailOptOut);

        // 5. League-wide reschedule addon emails — operational contacts, no registration.
        // Leagues are taken from the games actually in range, not from the job's first schedule row:
        // the latter answers "some league in this job", which is the wrong league the moment a job has
        // more than one.
        var leagueIds = await gameQuery
            .Select(s => s.LeagueId)
            .Distinct()
            .ToListAsync(ct);

        if (leagueIds.Count > 0)
        {
            var addons = await _context.Leagues
                .AsNoTracking()
                .Where(l => leagueIds.Contains(l.LeagueId))
                .Select(l => l.RescheduleEmailsToAddon)
                .ToListAsync(ct);

            foreach (var addonStr in addons)
                foreach (var email in EmailAddressRules.ParseDelimitedList(addonStr))
                    Add(null, email, false);
        }

        // 6. Job-level reschedule list — the same operational role as the league addon, set once on the
        // Communications tab where a director looks for email settings rather than per-league in LADT.
        // Purely additive: legacy also treated an EMPTY value as "send no reschedule mail to anyone",
        // but that gate belonged to the automatic on-game-move email, which no longer exists. This blast
        // is composed and sent by hand, so a blank config box must never silently swallow it.
        var jobRescheduleList = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.Rescheduleemaillist)
            .FirstOrDefaultAsync(ct);

        foreach (var email in EmailAddressRules.ParseDelimitedList(jobRescheduleList))
            Add(null, email, false);

        return byEmail.Values.ToList();
    }

    // Result codes mirror legacy [utility].[ScheduleAlterGSIPerGameDate]:
    //   1 = success
    //   2 = adjustment would create overlaps with non-target games
    //   3 = pre-GSI doesn't match the actual spacing of the first two games that day
    //   4 = postGSI out of [0,120]
    //   5 = preFirstGame and postFirstGame are in different calendar years
    //   6 = no games match the date/field filter
    //   7 = pre == post (nothing to change)
    //   8 = at least one target game isn't aligned to the pre-GSI grid
    public async Task<int> ExecuteWeatherAdjustmentAsync(
        Guid jobId, AdjustWeatherRequest request, CancellationToken ct = default)
    {
        if (request.PreFirstGame.Year != request.PostFirstGame.Year)
            return 5;
        if (request.PostGSI < 0 || request.PostGSI > 120)
            return 4;

        var preFirstGame = request.PreFirstGame;
        var postFirstGame = request.PostFirstGame;
        var preGSI = request.PreGSI;
        var postGSI = request.PostGSI;
        var day = preFirstGame.Date;
        var nextDay = day.AddDays(1);
        var hasFieldFilter = request.FieldIds.Count > 0;
        var fieldIds = request.FieldIds;

        // Load the target games (tracked — we may update them).
        var targetQuery = _context.Schedule
            .Where(s => s.JobId == jobId
                && s.GDate.HasValue
                && s.GDate.Value >= day
                && s.GDate.Value < nextDay
                && s.GDate.Value >= preFirstGame);
        if (hasFieldFilter)
            targetQuery = targetQuery.Where(s => s.FieldId.HasValue && fieldIds.Contains(s.FieldId.Value));

        var targetGames = await targetQuery.ToListAsync(ct);
        if (targetGames.Count == 0)
            return 6;

        // Legacy verifies preGSI against the first two distinct g_date values across ALL
        // fields that day for the job (not the field-filtered list) — preserve that.
        var firstTwoGDates = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId
                && s.GDate.HasValue
                && s.GDate.Value >= day
                && s.GDate.Value < nextDay
                && s.GDate.Value >= preFirstGame)
            .Select(s => s.GDate!.Value)
            .Distinct()
            .OrderBy(g => g)
            .Take(2)
            .ToListAsync(ct);

        if (firstTwoGDates.Count < 2 || (int)(firstTwoGDates[1] - firstTwoGDates[0]).TotalMinutes != preGSI)
            return 3;

        // Every target game must sit on the pre-GSI grid anchored at preFirstGame.
        var anyOffGrid = targetGames.Any(g =>
            (int)(preFirstGame - g.GDate!.Value).TotalMinutes % preGSI != 0);
        if (anyOffGrid)
            return 8;

        // No-op: same anchor, same spacing.
        if (preFirstGame == postFirstGame && preGSI == postGSI)
            return 7;

        // Project each target game to its new G_Date.
        var newTimes = targetGames
            .Select(g =>
            {
                var slotsFromPre = (int)(g.GDate!.Value - preFirstGame).TotalMinutes / preGSI;
                return (Game: g, NewGDate: postFirstGame.AddMinutes(slotsFromPre * postGSI));
            })
            .ToList();

        var postStarting = newTimes.Min(t => t.NewGDate);
        var postEnding = newTimes.Max(t => t.NewGDate).AddMinutes(postGSI);
        var targetGids = targetGames.Select(g => g.Gid).ToHashSet();

        // Wrap interference check + update so an interleaved insert can't slip a conflicting
        // game in between. Serializable matches the read-then-write invariant we need.
        await using var tx = await _context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);

        var interferenceQuery = _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId
                && s.GDate.HasValue
                && s.GDate.Value >= postStarting
                && s.GDate.Value < postEnding
                && !targetGids.Contains(s.Gid));
        if (hasFieldFilter)
            interferenceQuery = interferenceQuery.Where(s => s.FieldId.HasValue && fieldIds.Contains(s.FieldId.Value));

        if (await interferenceQuery.AnyAsync(ct))
            return 2;

        foreach (var (game, newGDate) in newTimes)
            game.GDate = newGDate;

        await _context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return 1;
    }

    // ── Dashboard ──

    public async Task<(int GameCount, int DivisionsScheduled)> GetSchedulingDashboardStatsAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var stats = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate.HasValue)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                GameCount = g.Count(),
                DivisionsScheduled = g.Where(s => s.DivId.HasValue).Select(s => s.DivId!.Value).Distinct().Count()
            })
            .FirstOrDefaultAsync(ct);

        return stats != null
            ? (stats.GameCount, stats.DivisionsScheduled)
            : (0, 0);
    }

    public async Task<Dictionary<Guid, int>> GetRoundRobinGameCountsByDivisionAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId
                && s.DivId.HasValue
                && s.T1Type == "T"
                && s.T2Type == "T")
            .GroupBy(s => s.DivId!.Value)
            .ToDictionaryAsync(g => g.Key, g => g.Count(), ct);
    }

    public async Task<Dictionary<Guid, int>> GetGameCountsByFieldIdsAsync(
        Guid jobId, List<Guid> fieldIds, CancellationToken ct = default)
    {
        return await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId
                && s.FieldId.HasValue
                && fieldIds.Contains(s.FieldId.Value))
            .GroupBy(s => s.FieldId!.Value)
            .ToDictionaryAsync(g => g.Key, g => g.Count(), ct);
    }

    // ── Private helpers ──

    /// <summary>
    /// Apply CADT filter (Club/Agegroup/Division/Team) with OR-union logic.
    /// </summary>
    private IQueryable<Domain.Entities.Schedule> ApplyCadtFilter(
        IQueryable<Domain.Entities.Schedule> query, ScheduleFilterRequest request)
    {
        var hasClubs = request.ClubNames is { Count: > 0 };
        var hasAgegroups = request.AgegroupIds is { Count: > 0 };
        var hasDivisions = request.DivisionIds is { Count: > 0 };
        var hasTeams = request.TeamIds is { Count: > 0 };

        if (!hasClubs && !hasAgegroups && !hasDivisions && !hasTeams)
            return query;

        // Bracket games belonging to the selected teams though not yet occupied by them.
        var selectedTeamIds = hasTeams
            ? _context.Teams.AsNoTracking()
                .Where(t => request.TeamIds!.Contains(t.TeamId))
                .Select(t => t.TeamId)
            : null;
        var teamSeedGids = hasTeams ? BracketGidsSeededFrom(selectedTeamIds!) : null;
        var teamSeatGids = hasTeams ? BracketGidsSeatedBy(selectedTeamIds!) : null;

        // For club filtering, we need to resolve club names to team IDs first.
        // This is done via a subquery join.
        if (hasClubs)
        {
            // Get team IDs belonging to selected clubs
            var clubTeamIds = _context.Teams
                .AsNoTracking()
                .Where(t => t.Active == true && t.ClubrepRegistrationid.HasValue)
                .Join(_context.Registrations.AsNoTracking(),
                    t => t.ClubrepRegistrationid!.Value,
                    r => r.RegistrationId,
                    (t, r) => new { t.TeamId, r.ClubName })
                .Where(x => request.ClubNames!.Contains(x.ClubName!))
                .Select(x => x.TeamId);

            var clubSeedGids = BracketGidsSeededFrom(clubTeamIds);
            var clubSeatGids = BracketGidsSeatedBy(clubTeamIds);

            query = query.Where(s =>
                clubTeamIds.Contains(s.T1Id!.Value) || clubTeamIds.Contains(s.T2Id!.Value)
                || clubSeedGids.Contains(s.Gid) || clubSeatGids.Contains(s.Gid)
                || (hasAgegroups && s.AgegroupId.HasValue && request.AgegroupIds!.Contains(s.AgegroupId.Value))
                || (hasDivisions && s.DivId.HasValue && request.DivisionIds!.Contains(s.DivId.Value))
                || (hasTeams && (request.TeamIds!.Contains(s.T1Id!.Value)
                    || request.TeamIds!.Contains(s.T2Id!.Value)
                    || teamSeedGids!.Contains(s.Gid)
                    || teamSeatGids!.Contains(s.Gid)))
            );
        }
        else
        {
            query = query.Where(s =>
                (hasAgegroups && s.AgegroupId.HasValue && request.AgegroupIds!.Contains(s.AgegroupId.Value))
                || (hasDivisions && s.DivId.HasValue && request.DivisionIds!.Contains(s.DivId.Value))
                || (hasTeams && (request.TeamIds!.Contains(s.T1Id!.Value)
                    || request.TeamIds!.Contains(s.T2Id!.Value)
                    || teamSeedGids!.Contains(s.Gid)
                    || teamSeatGids!.Contains(s.Gid)))
            );
        }

        return query;
    }

    /// <summary>
    /// Bracket games a team may play but does not yet OCCUPY. A bracket slot is minted empty and
    /// stays empty until seed resolution seats a team, so matching on T1Id/T2Id alone hides a
    /// team's championship games for the whole window between "bracket placed" and "pools
    /// scored" — and hides them permanently on any bracket that is reset. The membership lives in
    /// BracketSeeds: the (pool division, rank) feeding each slot. Matching on the seed division
    /// covers normal jobs (the pool is the game's own division) and reseeding tournaments (the
    /// pool is in a different agegroup) with one predicate.
    ///
    /// Applies ONLY to a slot that is still empty. While the pool is unscored no rank exists, so a
    /// team matches every leaf slot its pool feeds rather than the one it will land in — over-
    /// inclusive, but with no false negatives. The moment seed resolution seats a team the slot
    /// carries T1Id and the exact occupant match is authoritative; without the empty-slot guard
    /// this arm would keep firing and bury a resolved bracket in games the team never plays
    /// (measured: 10,282 spurious matches across 79 jobs and 6,563 teams on the dev database).
    /// Fed slots (Q/S/F targets of AdvancementFeeds) carry no seed row and are never matched.
    /// </summary>
    private IQueryable<int> BracketGidsSeededFrom(IQueryable<Guid> teamIds)
    {
        var seedDivIds = _context.Teams
            .AsNoTracking()
            .Where(t => teamIds.Contains(t.TeamId) && t.DivId.HasValue)
            .Select(t => t.DivId!.Value);

        return from bs in _context.BracketSeeds.AsNoTracking()
               join s in _context.Schedule.AsNoTracking() on bs.Gid equals s.Gid
               where (bs.T1SeedDivId.HasValue && seedDivIds.Contains(bs.T1SeedDivId.Value) && s.T1Id == null)
                  || (bs.T2SeedDivId.HasValue && seedDivIds.Contains(bs.T2SeedDivId.Value) && s.T2Id == null)
               select bs.Gid;
    }

    /// <summary>
    /// The other half of the same problem, for a team that LIVES in a bracket division rather than
    /// feeding one: a reseeding tournament's championship flight is its own agegroup whose teams
    /// (P01..P16) are seated in the flight's bracket games at their seed line. Those teams appear in
    /// no BracketSeeds row — the seed rows point back at the round-robin pools — so
    /// BracketGidsSeededFrom cannot see them, and while the bracket is unseeded neither can an
    /// occupant match. The seat is Schedule.TxNo == Teams.DivRank within the team's own division,
    /// the same derivation BracketSeedResolutionService uses to find the placeholder.
    ///
    /// Restricted to divisions with NO round-robin game, which is what makes a division a flight.
    /// In an ordinary job the bracket games share the pool's division and their TxNo are seed lines
    /// 1..N that collide numerically with DivRank 1..N — without this guard a team ranked 3 would
    /// match every bracket game holding a slot numbered 3, whoever actually plays it. Measured on
    /// the dev database that was 186 spurious matches across 113 teams in one job alone. There the
    /// slot is filled by seed resolution and the plain T1Id/T2Id match is already exact.
    /// </summary>
    private IQueryable<int> BracketGidsSeatedBy(IQueryable<Guid> teamIds)
    {
        var seats = _context.Teams
            .AsNoTracking()
            .Where(t => teamIds.Contains(t.TeamId) && t.DivId.HasValue)
            .Select(t => new { DivId = t.DivId!.Value, t.DivRank });

        return _context.Schedule
            .AsNoTracking()
            .Where(s => s.T1Type != null && s.T1Type != "T" && s.DivId.HasValue
                     && !_context.Schedule.Any(p => p.DivId == s.DivId && p.T1Type == "T")
                     && seats.Any(t => t.DivId == s.DivId!.Value
                          && ((s.T1No.HasValue && s.T1No.Value == t.DivRank)
                           || (s.T2No.HasValue && s.T2No.Value == t.DivRank))))
            .Select(s => s.Gid);
    }

    // ── Master Schedule ──

    public async Task<Dictionary<int, List<string>>> GetRefereeAssignmentsForGamesAsync(
        List<int> gids, CancellationToken ct = default)
    {
        if (gids.Count == 0) return new Dictionary<int, List<string>>();

        return await _context.RefGameAssigments
            .AsNoTracking()
            .Where(r => gids.Contains(r.GameId) && r.RefRegistration != null)
            .Select(r => new
            {
                r.GameId,
                Name = (r.RefRegistration!.User!.LastName ?? "") + ", " + (r.RefRegistration.User.FirstName ?? "")
            })
            .GroupBy(r => r.GameId)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.Select(x => x.Name).OrderBy(n => n).ToList(),
                ct);
    }

    public async Task<Dictionary<Guid, int>> GetMaxRoundByAgegroupAsync(
        Guid leagueId, string season, string year, CancellationToken ct = default)
    {
        var rows = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.LeagueId == leagueId
                && s.Season == season && s.Year == year
                && s.T1Type == "T"
                && s.Rnd != null
                && s.AgegroupId != null)
            .GroupBy(s => s.AgegroupId!.Value)
            .Select(g => new { AgegroupId = g.Key, MaxRnd = g.Max(s => (int)s.Rnd!) })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.AgegroupId, r => r.MaxRnd);
    }
}
