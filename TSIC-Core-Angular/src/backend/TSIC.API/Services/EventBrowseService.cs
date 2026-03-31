using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services;

/// <summary>
/// Service for public event browse/discovery endpoints.
/// </summary>
public sealed class EventBrowseService : IEventBrowseService
{
    private readonly IJobRepository _jobRepo;
    private readonly IPushNotificationRepository _pushRepo;

    public EventBrowseService(IJobRepository jobRepo, IPushNotificationRepository pushRepo)
    {
        _jobRepo = jobRepo;
        _pushRepo = pushRepo;
    }

    public async Task<List<EventListingDto>> GetActiveEventsAsync(CancellationToken ct = default)
    {
        return await _jobRepo.GetActivePublicEventsAsync(ct);
    }

    public async Task<List<EventAlertDto>> GetAlertsAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _pushRepo.GetAlertsByJobIdAsync(jobId, ct);
    }

    public async Task<List<EventDocDto>> GetDocsAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _jobRepo.GetJobDocsAsync(jobId, ct);
    }

    public async Task<GameClockConfigDto?> GetGameClockConfigAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _jobRepo.GetGameClockConfigAsync(jobId, ct);
    }
}
