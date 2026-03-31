using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Teams;

public sealed class TeamAttendanceService : ITeamAttendanceService
{
    private readonly ITeamAttendanceRepository _attendanceRepo;

    public TeamAttendanceService(ITeamAttendanceRepository attendanceRepo)
    {
        _attendanceRepo = attendanceRepo;
    }

    public async Task<List<AttendanceEventDto>> GetEventsAsync(Guid teamId, CancellationToken ct = default)
        => await _attendanceRepo.GetEventsAsync(teamId, ct);

    public async Task<AttendanceEventDto> CreateEventAsync(
        Guid teamId, string userId, CreateAttendanceEventRequest request, CancellationToken ct = default)
        => await _attendanceRepo.CreateEventAsync(teamId, userId, request, ct);

    public async Task<bool> DeleteEventAsync(int eventId, CancellationToken ct = default)
    {
        var deleted = await _attendanceRepo.DeleteEventAsync(eventId, ct);
        if (deleted) await _attendanceRepo.SaveChangesAsync(ct);
        return deleted;
    }

    public async Task<List<AttendanceRosterDto>> GetEventRosterAsync(int eventId, CancellationToken ct = default)
        => await _attendanceRepo.GetEventRosterAsync(eventId, ct);

    public async Task UpdateRsvpAsync(
        int eventId, UpdateRsvpRequest request, string userId, CancellationToken ct = default)
    {
        await _attendanceRepo.UpdateRsvpAsync(eventId, request.PlayerId, request.Present, userId, ct);
        await _attendanceRepo.SaveChangesAsync(ct);
    }

    public async Task<List<AttendanceHistoryDto>> GetPlayerHistoryAsync(
        Guid teamId, string userId, CancellationToken ct = default)
        => await _attendanceRepo.GetPlayerHistoryAsync(teamId, userId, ct);

    public async Task<List<AttendanceEventTypeDto>> GetEventTypesAsync(CancellationToken ct = default)
        => await _attendanceRepo.GetEventTypesAsync(ct);
}
