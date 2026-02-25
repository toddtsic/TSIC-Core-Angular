using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Referees;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for referee game assignment data access.
/// </summary>
public sealed class RefAssignmentRepository : IRefAssignmentRepository
{
    private readonly SqlDbContext _context;

    public RefAssignmentRepository(SqlDbContext context)
    {
        _context = context;
    }

    // ── Referee Roster ──

    public async Task<List<RefereeSummaryDto>> GetRefereesForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.RoleId == RoleConstants.Referee)
            .Join(_context.AspNetUsers, r => r.UserId, u => u.Id, (r, u) => new { r, u })
            .OrderBy(x => x.u.LastName)
            .ThenBy(x => x.u.FirstName)
            .Select(x => new RefereeSummaryDto
            {
                RegistrationId = x.r.RegistrationId,
                FirstName = x.u.FirstName ?? "",
                LastName = x.u.LastName ?? "",
                Email = x.u.Email,
                Cellphone = x.u.Cellphone,
                CertificationNumber = x.r.SportAssnId,
                CertificationExpiry = x.r.SportAssnIdexpDate,
                IsActive = x.r.BActive ?? false
            })
            .ToListAsync(ct);
    }

    // ── Assignments ──

    public async Task<List<GameRefAssignmentDto>> GetAllAssignmentsForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.RefGameAssigments
            .AsNoTracking()
            .Where(rga => rga.Game.JobId == jobId && rga.RefRegistrationId != null)
            .Select(rga => new GameRefAssignmentDto
            {
                Gid = rga.GameId,
                RefRegistrationId = rga.RefRegistrationId!.Value
            })
            .ToListAsync(ct);
    }

    public async Task<List<RefGameAssigments>> GetAssignmentsForGameAsync(int gid, CancellationToken ct = default)
    {
        return await _context.RefGameAssigments
            .Where(rga => rga.GameId == gid)
            .ToListAsync(ct);
    }

    public async Task ReplaceAssignmentsForGameAsync(int gid, List<Guid> refRegistrationIds, string auditUserId, CancellationToken ct = default)
    {
        // Delete existing
        var existing = await _context.RefGameAssigments
            .Where(rga => rga.GameId == gid)
            .ToListAsync(ct);

        _context.RefGameAssigments.RemoveRange(existing);

        // Insert new
        var now = DateTime.UtcNow;
        foreach (var regId in refRegistrationIds)
        {
            _context.RefGameAssigments.Add(new RefGameAssigments
            {
                GameId = gid,
                RefRegistrationId = regId,
                Modified = now,
                LebUserId = auditUserId
            });
        }

        // Update Schedule.RefCount
        var game = await _context.Schedule.FindAsync(new object[] { gid }, ct);
        if (game != null)
        {
            game.RefCount = refRegistrationIds.Count;
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAllAssignmentsForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var assignments = await _context.RefGameAssigments
            .Where(rga => rga.Game.JobId == jobId)
            .ToListAsync(ct);

        _context.RefGameAssigments.RemoveRange(assignments);

        // Reset RefCount on all games
        var games = await _context.Schedule
            .Where(s => s.JobId == jobId && s.RefCount > 0)
            .ToListAsync(ct);

        foreach (var game in games)
        {
            game.RefCount = 0;
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAllRefereeRegistrationsForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var registrations = await _context.Registrations
            .Where(r => r.JobId == jobId && r.RoleId == RoleConstants.Referee)
            .ToListAsync(ct);

        _context.Registrations.RemoveRange(registrations);
        await _context.SaveChangesAsync(ct);
    }

    // ── Schedule Search ──

    public async Task<RefScheduleFilterOptionsDto> GetRefScheduleFilterOptionsAsync(Guid jobId, CancellationToken ct = default)
    {
        var games = _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate != null);

        var gameDays = await games
            .Select(s => s.GDate!.Value.Date)
            .Distinct()
            .OrderBy(d => d)
            .Select(d => new FilterOptionDto
            {
                Value = d.ToString("yyyy-MM-dd"),
                Text = d.ToString("ddd M/d")
            })
            .ToListAsync(ct);

        var gameTimes = await games
            .Select(s => s.GDate!.Value.TimeOfDay)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync(ct);

        var timeOptions = gameTimes
            .Select(t => new FilterOptionDto
            {
                Value = t.ToString(@"hh\:mm"),
                Text = DateTime.Today.Add(t).ToString("h:mm tt")
            })
            .ToList();

        var agegroups = await games
            .Where(s => s.AgegroupId != null && s.AgegroupName != null)
            .Select(s => new { s.AgegroupId, s.AgegroupName })
            .Distinct()
            .OrderBy(a => a.AgegroupName)
            .Select(a => new FilterOptionDto
            {
                Value = a.AgegroupId!.Value.ToString(),
                Text = a.AgegroupName!
            })
            .ToListAsync(ct);

        var fields = await games
            .Where(s => s.FieldId != null && s.FName != null)
            .Select(s => new { s.FieldId, s.FName })
            .Distinct()
            .OrderBy(f => f.FName)
            .Select(f => new FilterOptionDto
            {
                Value = f.FieldId!.Value.ToString(),
                Text = f.FName!
            })
            .ToListAsync(ct);

        return new RefScheduleFilterOptionsDto
        {
            GameDays = gameDays,
            GameTimes = timeOptions,
            Agegroups = agegroups,
            Fields = fields
        };
    }

    public async Task<List<RefScheduleGameDto>> SearchScheduleAsync(Guid jobId, RefScheduleSearchRequest request, CancellationToken ct = default)
    {
        var query = _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate != null);

        // Apply filters
        if (request.GameDays?.Count > 0)
        {
            var dates = request.GameDays
                .Select(d => DateTime.Parse(d).Date)
                .ToList();
            query = query.Where(s => dates.Contains(s.GDate!.Value.Date));
        }

        if (request.GameTimes?.Count > 0)
        {
            var times = request.GameTimes
                .Select(t => TimeSpan.Parse(t))
                .ToList();
            query = query.Where(s => times.Contains(s.GDate!.Value.TimeOfDay));
        }

        if (request.AgegroupIds?.Count > 0)
        {
            query = query.Where(s => s.AgegroupId != null && request.AgegroupIds.Contains(s.AgegroupId.Value));
        }

        if (request.FieldIds?.Count > 0)
        {
            query = query.Where(s => s.FieldId != null && request.FieldIds.Contains(s.FieldId.Value));
        }

        // Load games with ref assignments
        var games = await query
            .Include(s => s.RefGameAssigments)
            .OrderBy(s => s.GDate)
            .ThenBy(s => s.FName)
            .ToListAsync(ct);

        return games.Select(s => new RefScheduleGameDto
        {
            Gid = s.Gid,
            GameDate = s.GDate!.Value,
            FieldName = s.FName,
            FieldId = s.FieldId,
            AgegroupName = s.AgegroupName,
            AgegroupColor = null, // Will be enriched below
            DivName = s.DivName,
            T1Name = s.T1Name,
            T2Name = s.T2Name,
            GameType = s.T1Type == "T" && s.T2Type == "T" ? null : (s.T1Type ?? s.T2Type),
            AssignedRefIds = s.RefGameAssigments
                .Where(rga => rga.RefRegistrationId != null)
                .Select(rga => rga.RefRegistrationId!.Value)
                .ToList()
        }).ToList();
    }

    public async Task<List<RefGameDetailsDto>> GetGameRefDetailsAsync(int gid, Guid jobId, CancellationToken ct = default)
    {
        // Get the refs assigned to this game
        var gameRefs = await _context.RefGameAssigments
            .AsNoTracking()
            .Where(rga => rga.GameId == gid && rga.RefRegistrationId != null)
            .Select(rga => rga.RefRegistrationId!.Value)
            .ToListAsync(ct);

        if (gameRefs.Count == 0)
            return [];

        // For each ref, get ALL their game assignments in this job
        var result = new List<RefGameDetailsDto>();

        foreach (var refRegId in gameRefs)
        {
            var refUser = await _context.Registrations
                .AsNoTracking()
                .Where(r => r.RegistrationId == refRegId)
                .Join(_context.AspNetUsers, r => r.UserId, u => u.Id, (r, u) => u)
                .FirstOrDefaultAsync(ct);

            var refGames = await _context.RefGameAssigments
                .AsNoTracking()
                .Where(rga => rga.RefRegistrationId == refRegId && rga.Game.JobId == jobId)
                .OrderBy(rga => rga.Game.GDate)
                .Select(rga => new RefGameDetailRow
                {
                    Gid = rga.GameId,
                    GameDate = rga.Game.GDate ?? DateTime.MinValue,
                    FieldName = rga.Game.FName ?? "",
                    AgegroupName = rga.Game.AgegroupName ?? "",
                    DivName = rga.Game.DivName ?? "",
                    T1Name = rga.Game.T1Name ?? "",
                    T2Name = rga.Game.T2Name ?? ""
                })
                .ToListAsync(ct);

            result.Add(new RefGameDetailsDto
            {
                RefName = refUser != null ? $"{refUser.LastName}, {refUser.FirstName}" : "Unknown",
                RegistrationId = refRegId,
                Games = refGames
            });
        }

        return result;
    }

    // ── Calendar ──

    public async Task<List<RefereeCalendarEventDto>> GetCalendarEventsAsync(Guid jobId, CancellationToken ct = default)
    {
        // Load all assignments with their game + ref user data
        var assignments = await _context.RefGameAssigments
            .AsNoTracking()
            .Where(rga => rga.Game.JobId == jobId && rga.RefRegistrationId != null && rga.Game.GDate != null)
            .Select(rga => new
            {
                rga.Id,
                rga.GameId,
                rga.RefRegistrationId,
                GameDate = rga.Game.GDate!.Value,
                FieldId = rga.Game.FieldId,
                FieldName = rga.Game.FName,
                AgegroupName = rga.Game.AgegroupName,
                DivName = rga.Game.DivName,
                T1Name = rga.Game.T1Name,
                T2Name = rga.Game.T2Name,
            })
            .OrderBy(a => a.GameDate)
            .ToListAsync(ct);

        if (assignments.Count == 0)
            return [];

        // Load agegroup colors
        var agegroupNames = assignments
            .Where(a => a.AgegroupName != null)
            .Select(a => a.AgegroupName!)
            .Distinct()
            .ToList();

        var agegroupColors = await _context.Agegroups
            .AsNoTracking()
            .Where(ag => ag.LeagueId != Guid.Empty && agegroupNames.Contains(ag.AgegroupName ?? ""))
            .Select(ag => new { ag.AgegroupName, ag.Color })
            .ToListAsync(ct);

        var colorMap = agegroupColors
            .Where(ac => ac.AgegroupName != null)
            .GroupBy(ac => ac.AgegroupName!)
            .ToDictionary(g => g.Key, g => g.First().Color ?? "#1976d2");

        // Load ref user info
        var refRegIds = assignments.Select(a => a.RefRegistrationId!.Value).Distinct().ToList();
        var refUsers = await _context.Registrations
            .AsNoTracking()
            .Where(r => refRegIds.Contains(r.RegistrationId))
            .Join(_context.AspNetUsers, r => r.UserId, u => u.Id, (r, u) => new
            {
                r.RegistrationId,
                UserId = u.Id,
                u.FirstName,
                u.LastName
            })
            .ToListAsync(ct);

        var refUserMap = refUsers.ToDictionary(r => r.RegistrationId);

        // Group assignments by GameId to calculate RefsWith and EndTime
        var gameGroups = assignments.GroupBy(a => a.GameId).ToDictionary(g => g.Key, g => g.ToList());

        // Build end-time map: for each field+date, find the next game's start time
        var fieldDateGroups = assignments
            .Where(a => a.FieldId != null)
            .GroupBy(a => a.FieldId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(a => a.GameDate).Distinct().OrderBy(d => d).ToList());

        var events = new List<RefereeCalendarEventDto>();

        foreach (var assignment in assignments)
        {
            if (!refUserMap.TryGetValue(assignment.RefRegistrationId!.Value, out var refUser))
                continue;

            // Calculate EndTime
            var endTime = assignment.GameDate.AddMinutes(50); // default
            if (assignment.FieldId != null && fieldDateGroups.TryGetValue(assignment.FieldId.Value, out var fieldDates))
            {
                var nextDate = fieldDates.FirstOrDefault(d => d > assignment.GameDate);
                if (nextDate != default)
                {
                    endTime = nextDate;
                }
            }

            // Calculate RefsWith
            var gameAssignments = gameGroups.GetValueOrDefault(assignment.GameId) ?? [];
            var otherRefs = gameAssignments
                .Where(a => a.RefRegistrationId != assignment.RefRegistrationId)
                .Select(a =>
                {
                    if (refUserMap.TryGetValue(a.RefRegistrationId!.Value, out var other))
                        return $"{other.LastName}, {other.FirstName}";
                    return null;
                })
                .Where(n => n != null)
                .ToList();

            var refsWith = otherRefs.Count > 0 ? string.Join(", ", otherRefs) : "solo";
            var color = assignment.AgegroupName != null && colorMap.TryGetValue(assignment.AgegroupName, out var c) ? c : "#1976d2";

            events.Add(new RefereeCalendarEventDto
            {
                Id = assignment.Id,
                GameId = assignment.GameId,
                Subject = $"{refUser.LastName}, {refUser.FirstName} - {assignment.T1Name ?? "TBD"} vs {assignment.T2Name ?? "TBD"}",
                StartTime = assignment.GameDate,
                EndTime = endTime,
                Location = assignment.FieldName ?? "",
                Description = $"{assignment.AgegroupName ?? ""} - {assignment.DivName ?? ""}".Trim(' ', '-'),
                FieldId = assignment.FieldId,
                FieldName = assignment.FieldName,
                RefereeId = refUser.UserId,
                RefereeFirstName = refUser.FirstName ?? "",
                RefereeLastName = refUser.LastName ?? "",
                AgegroupName = assignment.AgegroupName,
                DivName = assignment.DivName,
                Team1 = assignment.T1Name,
                Team2 = assignment.T2Name,
                Color = color,
                RefsWith = refsWith
            });
        }

        return events;
    }

    // ── Copy Support ──

    public async Task<List<Schedule>> GetGamesOnFieldForDateAsync(Guid fieldId, DateTime gameDate, Guid jobId, CancellationToken ct = default)
    {
        var dateOnly = gameDate.Date;
        return await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId
                && s.FieldId == fieldId
                && s.GDate != null
                && s.GDate.Value.Date == dateOnly)
            .OrderBy(s => s.GDate)
            .ToListAsync(ct);
    }

    // ── Persistence ──

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }
}
