using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Data.SqlDbContext;

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
        // 1. Get team's current TeamName + ClubrepRegistrationid
        var team = await _context.Teams
            .AsNoTracking()
            .Where(t => t.TeamId == teamId)
            .Select(t => new { t.TeamName, t.ClubrepRegistrationid })
            .FirstOrDefaultAsync(ct);
        if (team == null) return;

        // 2. Get club name from Registration (if team has club rep context)
        string? clubName = null;
        if (team.ClubrepRegistrationid.HasValue)
        {
            clubName = await _context.Registrations
                .AsNoTracking()
                .Where(r => r.RegistrationId == team.ClubrepRegistrationid.Value)
                .Select(r => r.ClubName)
                .FirstOrDefaultAsync(ct);
        }

        // 3. Get job's BShowTeamNameOnlyInSchedules setting
        var showTeamNameOnly = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.BShowTeamNameOnlyInSchedules)
            .FirstOrDefaultAsync(ct);

        // 4. Compose the display name (matches write-time format from scheduler)
        var displayName = (!string.IsNullOrEmpty(clubName) && !showTeamNameOnly)
            ? $"{clubName}:{team.TeamName}"
            : team.TeamName ?? "";

        // 5. Update round-robin games only (T1Type/T2Type == "T")
        var schedules = await _context.Schedule
            .Where(s => (s.T1Id == teamId && s.T1Type == "T")
                     || (s.T2Id == teamId && s.T2Type == "T"))
            .ToListAsync(ct);

        foreach (var s in schedules)
        {
            if (s.T1Id == teamId && s.T1Type == "T") s.T1Name = displayName;
            if (s.T2Id == teamId && s.T2Type == "T") s.T2Name = displayName;
        }

        if (schedules.Count > 0)
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

        var game = await _context.Schedule.FindAsync(new object[] { gid }, ct);
        if (game != null)
            _context.Schedule.Remove(game);
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

        var games = await _context.Schedule
            .Where(s => gids.Contains(s.Gid))
            .ToListAsync(ct);
        _context.Schedule.RemoveRange(games);
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
            .Where(s => s.JobId == jobId && s.GDate.HasValue);

        query = ApplyCadtFilter(query, request);

        if (request.GameDays is { Count: > 0 })
        {
            var days = request.GameDays.Select(d => d.Date).ToList();
            query = query.Where(s => s.GDate.HasValue && days.Contains(s.GDate.Value.Date));
        }

        if (request.FieldIds is { Count: > 0 })
            query = query.Where(s => s.FieldId.HasValue && request.FieldIds.Contains(s.FieldId.Value));

        if (request.UnscoredOnly == true)
            query = query.Where(s => s.T1Score == null && s.T2Score == null);

        return await query.OrderBy(s => s.GDate).ToListAsync(ct);
    }

    public async Task<ScheduleFilterOptionsDto> GetScheduleFilterOptionsAsync(
        Guid jobId, CancellationToken ct = default)
    {
        // 1. Get all team IDs that appear in scheduled games for this job
        var scheduledTeamIds = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate.HasValue)
            .SelectMany(s => new[] { s.T1Id, s.T2Id })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToListAsync(ct);

        if (scheduledTeamIds.Count == 0)
        {
            return new ScheduleFilterOptionsDto
            {
                Clubs = [],
                GameDays = [],
                Fields = []
            };
        }

        // 2. Get teams with their club rep registration for club name resolution
        var teams = await _context.Teams
            .AsNoTracking()
            .Where(t => scheduledTeamIds.Contains(t.TeamId) && t.Active == true)
            .Select(t => new
            {
                t.TeamId,
                t.TeamName,
                t.AgegroupId,
                AgegroupName = t.Agegroup.AgegroupName,
                AgegroupColor = t.Agegroup.Color,
                t.DivId,
                DivName = t.Div != null ? t.Div.DivName : null,
                t.ClubrepRegistrationid
            })
            .ToListAsync(ct);

        // 3. Resolve club names from club rep registrations
        var clubRepIds = teams
            .Where(t => t.ClubrepRegistrationid.HasValue)
            .Select(t => t.ClubrepRegistrationid!.Value)
            .Distinct()
            .ToList();

        var clubNameMap = clubRepIds.Count > 0
            ? await _context.Registrations
                .AsNoTracking()
                .Where(r => clubRepIds.Contains(r.RegistrationId))
                .ToDictionaryAsync(r => r.RegistrationId, r => r.ClubName ?? "Unknown", ct)
            : new Dictionary<Guid, string>();

        // 4. Build CADT tree: Club → Agegroup → Division → Team
        var cadtTree = teams
            .Select(t => new
            {
                ClubName = t.ClubrepRegistrationid.HasValue
                    && clubNameMap.TryGetValue(t.ClubrepRegistrationid.Value, out var cn)
                    ? cn : "Unaffiliated",
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

        // 6. Get distinct fields used in games
        var fields = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.FieldId.HasValue && s.GDate.HasValue)
            .Select(s => s.FieldId!.Value)
            .Distinct()
            .Join(_context.Fields.AsNoTracking(),
                fid => fid,
                f => f.FieldId,
                (fid, f) => new FieldSummaryDto
                {
                    FieldId = f.FieldId,
                    FName = f.FName ?? ""
                })
            .OrderBy(f => f.FName)
            .ToListAsync(ct);

        return new ScheduleFilterOptionsDto
        {
            Clubs = cadtTree,
            GameDays = gameDays,
            Fields = fields
        };
    }

    public async Task<List<Domain.Entities.Schedule>> GetTeamGamesAsync(
        Guid teamId, CancellationToken ct = default)
    {
        return await _context.Schedule
            .AsNoTracking()
            .Include(s => s.Field)
            .Where(s => (s.T1Id == teamId || s.T2Id == teamId) && s.GDate.HasValue)
            .OrderBy(s => s.GDate)
            .ToListAsync(ct);
    }

    public async Task<List<Domain.Entities.Schedule>> GetBracketGamesAsync(
        Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default)
    {
        var bracketTypes = new[] { "Q", "S", "F", "X", "Y", "Z" };
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

        return await query.OrderBy(s => s.GDate).ToListAsync(ct);
    }

    public async Task<List<ContactDto>> GetContactsAsync(
        Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default)
    {
        // 1. Get team IDs from the filtered schedule
        var gameQuery = _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate.HasValue);

        gameQuery = ApplyCadtFilter(gameQuery, request);

        var teamIds = await gameQuery
            .SelectMany(s => new[] { s.T1Id, s.T2Id })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToListAsync(ct);

        if (teamIds.Count == 0) return [];

        // 2. Get staff registrations assigned to these teams, with team/agegroup/division navigations
        return await _context.Registrations
            .AsNoTracking()
            .Include(r => r.AssignedTeam)
                .ThenInclude(t => t!.Agegroup)
            .Include(r => r.AssignedTeam)
                .ThenInclude(t => t!.Div)
            .Include(r => r.User)
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

        // Get BHideContacts from the primary league
        var hideContacts = await _context.JobLeagues
            .AsNoTracking()
            .Where(jl => jl.JobId == jobId && jl.BIsPrimary)
            .Select(jl => jl.League.BHideContacts)
            .FirstOrDefaultAsync(ct);

        return (
            jobFlags?.AllowPublic ?? false,
            hideContacts,
            jobFlags?.SportName ?? "Soccer"
        );
    }

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

            query = query.Where(s =>
                clubTeamIds.Contains(s.T1Id!.Value) || clubTeamIds.Contains(s.T2Id!.Value)
                || (hasAgegroups && s.AgegroupId.HasValue && request.AgegroupIds!.Contains(s.AgegroupId.Value))
                || (hasDivisions && s.DivId.HasValue && request.DivisionIds!.Contains(s.DivId.Value))
                || (hasTeams && (request.TeamIds!.Contains(s.T1Id!.Value) || request.TeamIds!.Contains(s.T2Id!.Value)))
            );
        }
        else
        {
            query = query.Where(s =>
                (hasAgegroups && s.AgegroupId.HasValue && request.AgegroupIds!.Contains(s.AgegroupId.Value))
                || (hasDivisions && s.DivId.HasValue && request.DivisionIds!.Contains(s.DivId.Value))
                || (hasTeams && (request.TeamIds!.Contains(s.T1Id!.Value) || request.TeamIds!.Contains(s.T2Id!.Value)))
            );
        }

        return query;
    }
}
