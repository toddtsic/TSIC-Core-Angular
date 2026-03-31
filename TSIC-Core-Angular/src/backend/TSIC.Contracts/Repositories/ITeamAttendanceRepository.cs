using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Repositories;

public interface ITeamAttendanceRepository
{
    Task<List<AttendanceEventDto>> GetEventsAsync(Guid teamId, CancellationToken ct = default);
    Task<AttendanceEventDto> CreateEventAsync(Guid teamId, string userId, CreateAttendanceEventRequest request, CancellationToken ct = default);
    Task<bool> DeleteEventAsync(int eventId, CancellationToken ct = default);
    Task<List<AttendanceRosterDto>> GetEventRosterAsync(int eventId, CancellationToken ct = default);
    Task UpdateRsvpAsync(int eventId, string playerId, bool present, string userId, CancellationToken ct = default);
    Task<List<AttendanceHistoryDto>> GetPlayerHistoryAsync(Guid teamId, string userId, CancellationToken ct = default);
    Task<List<AttendanceEventTypeDto>> GetEventTypesAsync(CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
