using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.API.Services.Shared.Jobs;

namespace TSIC.API.Services.Shared.Bulletins;

/// <summary>
/// Service for managing bulletin business logic including URL translation and token substitution.
/// </summary>
public class BulletinService : IBulletinService
{
    // Token constants for text substitution
    private const string JobNameToken = "!JOBNAME";
    private const string UslaxDateToken = "!USLAXVALIDTHROUGHDATE";

    private readonly IJobLookupService _jobLookupService;
    private readonly IBulletinRepository _bulletinRepository;
    private readonly ILogger<BulletinService> _logger;

    public BulletinService(
        IJobLookupService jobLookupService,
        IBulletinRepository bulletinRepository,
        ILogger<BulletinService> logger)
    {
        _jobLookupService = jobLookupService;
        _bulletinRepository = bulletinRepository;
        _logger = logger;
    }

    public async Task<List<BulletinDto>> GetActiveBulletinsForJobAsync(string jobPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing bulletins for job: {JobPath}", jobPath);

        var jobMetadata = await _jobLookupService.GetJobMetadataAsync(jobPath);
        if (jobMetadata == null)
        {
            _logger.LogWarning("Job not found: {JobPath}", jobPath);
            return new List<BulletinDto>();
        }

        var bulletins = await _bulletinRepository.GetActiveBulletinsForJobAsync(jobMetadata.JobId, cancellationToken);

        // Process bulletin title and text with job-level token replacement
        var jobName = jobMetadata.JobName;
        var uslaxDate = jobMetadata.USLaxNumberValidThroughDate?.ToString("M/d/yy") ?? string.Empty;

        var processedBulletins = new List<BulletinDto>();
        foreach (var bulletin in bulletins)
        {
            var processedTitle = (bulletin.Title ?? string.Empty)
                .Replace(JobNameToken, jobName, StringComparison.OrdinalIgnoreCase)
                .Replace(UslaxDateToken, uslaxDate, StringComparison.OrdinalIgnoreCase);

            var processedText = (bulletin.Text ?? string.Empty)
                .Replace(JobNameToken, jobName, StringComparison.OrdinalIgnoreCase)
                .Replace(UslaxDateToken, uslaxDate, StringComparison.OrdinalIgnoreCase);

            processedBulletins.Add(new BulletinDto
            {
                BulletinId = bulletin.BulletinId,
                Title = processedTitle,
                Text = processedText,
                StartDate = bulletin.StartDate,
                EndDate = bulletin.EndDate,
                CreateDate = bulletin.CreateDate
            });
        }

        return processedBulletins;
    }
}
