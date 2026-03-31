using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Services;

public interface ITeamAttendanceService
{
    Task<List<AttendanceEventDto>> GetEventsAsync(Guid teamId, CancellationToken ct = default);
    Task<AttendanceEventDto> CreateEventAsync(Guid teamId, string userId, CreateAttendanceEventRequest request, CancellationToken ct = default);
    Task<bool> DeleteEventAsync(int eventId, CancellationToken ct = default);
    Task<List<AttendanceRosterDto>> GetEventRosterAsync(int eventId, CancellationToken ct = default);
    Task UpdateRsvpAsync(int eventId, UpdateRsvpRequest request, string userId, CancellationToken ct = default);
    Task<List<AttendanceHistoryDto>> GetPlayerHistoryAsync(Guid teamId, string userId, CancellationToken ct = default);
    Task<List<AttendanceEventTypeDto>> GetEventTypesAsync(CancellationToken ct = default);
}
