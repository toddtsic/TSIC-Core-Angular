using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for public event browse/discovery — used by mobile apps and public website.
/// </summary>
public interface IEventBrowseService
{
    Task<List<EventListingDto>> GetActiveEventsAsync(CancellationToken ct = default);
    Task<List<EventAlertDto>> GetAlertsAsync(Guid jobId, CancellationToken ct = default);
    Task<List<EventDocDto>> GetDocsAsync(Guid jobId, CancellationToken ct = default);
    Task<GameClockConfigDto?> GetGameClockConfigAsync(Guid jobId, CancellationToken ct = default);
    Task<GameClockAvailableGameTimesDto> GetActiveGamesAsync(Guid jobId, DateTime? preferredGameDate, CancellationToken ct = default);
}
