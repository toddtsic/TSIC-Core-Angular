using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class TeamAttendanceRepository : ITeamAttendanceRepository
{
    private readonly SqlDbContext _context;

    public TeamAttendanceRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<AttendanceEventDto>> GetEventsAsync(Guid teamId, CancellationToken ct = default)
    {
        return await _context.TeamAttendanceEvents
            .AsNoTracking()
            .Where(e => e.TeamId == teamId)
            .OrderByDescending(e => e.EventDate)
            .Select(e => new AttendanceEventDto
            {
                EventId = e.EventId,
                TeamId = e.TeamId,
                Comment = e.Comment,
                EventTypeId = e.EventTypeId,
                EventType = e.EventType.AttendanceType,
                EventDate = e.EventDate,
                EventLocation = e.EventLocation,
                Present = e.TeamAttendanceRecords.Count(r => r.Present),
                NotPresent = e.TeamAttendanceRecords.Count(r => !r.Present),
                Unknown = 0,
                CreatorUserId = e.LebUserId
            })
            .ToListAsync(ct);
    }

    public async Task<AttendanceEventDto> CreateEventAsync(
        Guid teamId, string userId, CreateAttendanceEventRequest request, CancellationToken ct = default)
    {
        var entity = new TeamAttendanceEvents
        {
            TeamId = teamId,
            Comment = request.Comment,
            EventTypeId = request.EventTypeId,
            EventDate = request.EventDate,
            EventLocation = request.EventLocation,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };
        _context.TeamAttendanceEvents.Add(entity);
        await _context.SaveChangesAsync(ct);

        var typeName = await _context.TeamAttendanceTypes
            .AsNoTracking()
            .Where(t => t.Id == request.EventTypeId)
            .Select(t => t.AttendanceType)
            .FirstOrDefaultAsync(ct);

        return new AttendanceEventDto
        {
            EventId = entity.EventId,
            TeamId = teamId,
            Comment = entity.Comment,
            EventTypeId = entity.EventTypeId,
            EventType = typeName,
            EventDate = entity.EventDate,
            EventLocation = entity.EventLocation,
            Present = 0,
            NotPresent = 0,
            Unknown = 0,
            CreatorUserId = userId
        };
    }

    public async Task<bool> DeleteEventAsync(int eventId, CancellationToken ct = default)
    {
        var entity = await _context.TeamAttendanceEvents
            .Include(e => e.TeamAttendanceRecords)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (entity == null) return false;

        _context.TeamAttendanceRecords.RemoveRange(entity.TeamAttendanceRecords);
        _context.TeamAttendanceEvents.Remove(entity);
        return true;
    }

    public async Task<List<AttendanceRosterDto>> GetEventRosterAsync(int eventId, CancellationToken ct = default)
    {
        return await _context.TeamAttendanceRecords
            .AsNoTracking()
            .Where(r => r.EventId == eventId)
            .Select(r => new AttendanceRosterDto
            {
                AttendanceId = r.AttendanceId,
                PlayerId = r.PlayerId,
                PlayerFirstName = r.Player.FirstName,
                PlayerLastName = r.Player.LastName,
                Present = r.Present,
                UniformNo = null,
                HeadshotUrl = null
            })
            .ToListAsync(ct);
    }

    public async Task UpdateRsvpAsync(
        int eventId, string playerId, bool present, string userId, CancellationToken ct = default)
    {
        var existing = await _context.TeamAttendanceRecords
            .FirstOrDefaultAsync(r => r.EventId == eventId && r.PlayerId == playerId, ct);

        if (existing != null)
        {
            existing.Present = present;
            existing.LebUserId = userId;
            existing.Modified = DateTime.UtcNow;
        }
        else
        {
            _context.TeamAttendanceRecords.Add(new TeamAttendanceRecords
            {
                EventId = eventId,
                PlayerId = playerId,
                Present = present,
                LebUserId = userId,
                Modified = DateTime.UtcNow
            });
        }
    }

    public async Task<List<AttendanceHistoryDto>> GetPlayerHistoryAsync(
        Guid teamId, string userId, CancellationToken ct = default)
    {
        return await _context.TeamAttendanceRecords
            .AsNoTracking()
            .Where(r => r.PlayerId == userId && r.Event.TeamId == teamId)
            .OrderByDescending(r => r.Event.EventDate)
            .Select(r => new AttendanceHistoryDto
            {
                EventDate = r.Event.EventDate,
                EventType = r.Event.EventType.AttendanceType,
                Present = r.Present
            })
            .ToListAsync(ct);
    }

    public async Task<List<AttendanceEventTypeDto>> GetEventTypesAsync(CancellationToken ct = default)
    {
        return await _context.TeamAttendanceTypes
            .AsNoTracking()
            .Select(t => new AttendanceEventTypeDto
            {
                Id = t.Id,
                AttendanceType = t.AttendanceType
            })
            .ToListAsync(ct);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }
}
