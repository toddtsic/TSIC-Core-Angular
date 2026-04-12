using System.Security.Claims;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Bulletin;
using TSIC.Contracts.Repositories;
using TSIC.API.Services.Shared.Bulletins.TokenResolution;
using TSIC.API.Services.Shared.Jobs;
using BulletinEntity = TSIC.Domain.Entities.Bulletins;

namespace TSIC.API.Services.Shared.Bulletins;

/// <summary>
/// Service for managing bulletin business logic.
/// Public fetch path: text-token substitution (!JOBNAME etc.) + {{TOKEN}} resolution via BulletinTokenRegistry.
/// Legacy URL translation is still handled by the frontend TranslateLegacyUrlsPipe.
/// </summary>
public class BulletinService : IBulletinService
{
    private const string JobNameToken = "!JOBNAME";
    private const string UslaxDateToken = "!USLAXVALIDTHROUGHDATE";

    private readonly IJobLookupService _jobLookupService;
    private readonly IJobRepository _jobRepository;
    private readonly IBulletinRepository _bulletinRepository;
    private readonly BulletinTokenRegistry _tokenRegistry;
    private readonly ILogger<BulletinService> _logger;

    public BulletinService(
        IJobLookupService jobLookupService,
        IJobRepository jobRepository,
        IBulletinRepository bulletinRepository,
        BulletinTokenRegistry tokenRegistry,
        ILogger<BulletinService> logger)
    {
        _jobLookupService = jobLookupService;
        _jobRepository = jobRepository;
        _bulletinRepository = bulletinRepository;
        _tokenRegistry = tokenRegistry;
        _logger = logger;
    }

    public async Task<List<BulletinDto>> GetActiveBulletinsForJobAsync(string jobPath, ClaimsPrincipal? user, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing bulletins for job: {JobPath}", jobPath);

        var jobMetadata = await _jobLookupService.GetJobMetadataAsync(jobPath);
        if (jobMetadata == null)
        {
            _logger.LogWarning("Job not found: {JobPath}", jobPath);
            return new List<BulletinDto>();
        }

        var bulletins = await _bulletinRepository.GetActiveBulletinsForJobAsync(jobMetadata.JobId, cancellationToken);

        var jobName = jobMetadata.JobName;
        var uslaxDate = jobMetadata.USLaxNumberValidThroughDate?.ToString("M/d/yy") ?? string.Empty;

        // Build token context (pulse + auth state) once; reused across all bulletins in this response.
        var pulse = await _jobRepository.GetJobPulseAsync(jobPath, cancellationToken);
        TokenContext? tokenCtx = null;
        if (pulse != null)
        {
            tokenCtx = new TokenContext
            {
                JobPath = jobPath,
                Job = jobMetadata,
                Pulse = pulse,
                IsAuthenticated = user?.Identity?.IsAuthenticated ?? false,
                Role = user?.FindFirst(ClaimTypes.Role)?.Value
            };
        }

        var processedBulletins = new List<BulletinDto>();
        foreach (var bulletin in bulletins)
        {
            var title = ReplaceTextTokens(bulletin.Title ?? string.Empty, jobName, uslaxDate);
            var text = ReplaceTextTokens(bulletin.Text ?? string.Empty, jobName, uslaxDate);

            if (tokenCtx != null)
            {
                title = _tokenRegistry.ResolveTokens(title, tokenCtx);
                text = _tokenRegistry.ResolveTokens(text, tokenCtx);
            }

            processedBulletins.Add(new BulletinDto
            {
                BulletinId = bulletin.BulletinId,
                Title = title,
                Text = text,
                StartDate = bulletin.StartDate,
                EndDate = bulletin.EndDate,
                CreateDate = bulletin.CreateDate
            });
        }

        return processedBulletins;
    }

    public async Task<List<BulletinAdminDto>> GetAllBulletinsForJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _bulletinRepository.GetAllBulletinsForJobAsync(jobId, cancellationToken);
    }

    public async Task<BulletinAdminDto> CreateBulletinAsync(Guid jobId, string userId, CreateBulletinRequest request, CancellationToken cancellationToken = default)
    {
        if (request.StartDate.HasValue && request.EndDate.HasValue && request.EndDate < request.StartDate)
        {
            throw new InvalidOperationException("End date must be on or after start date.");
        }

        var bulletin = new BulletinEntity
        {
            BulletinId = Guid.NewGuid(),
            JobId = jobId,
            Title = request.Title,
            Text = request.Text,
            Active = request.Active,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            LebUserId = userId,
            CreateDate = DateTime.UtcNow,
            Modified = DateTime.UtcNow
        };

        _bulletinRepository.Add(bulletin);
        await _bulletinRepository.SaveChangesAsync(cancellationToken);

        return new BulletinAdminDto
        {
            BulletinId = bulletin.BulletinId,
            Title = bulletin.Title,
            Text = bulletin.Text,
            Active = bulletin.Active,
            StartDate = bulletin.StartDate,
            EndDate = bulletin.EndDate,
            CreateDate = bulletin.CreateDate,
            Modified = bulletin.Modified,
            ModifiedByUsername = null
        };
    }

    public async Task<BulletinAdminDto> UpdateBulletinAsync(Guid bulletinId, Guid jobId, string userId, UpdateBulletinRequest request, CancellationToken cancellationToken = default)
    {
        var bulletin = await _bulletinRepository.GetByIdAsync(bulletinId, cancellationToken);
        if (bulletin == null)
        {
            throw new InvalidOperationException($"Bulletin with ID {bulletinId} not found.");
        }

        if (bulletin.JobId != jobId)
        {
            throw new InvalidOperationException("Bulletin does not belong to the current job.");
        }

        if (request.StartDate.HasValue && request.EndDate.HasValue && request.EndDate < request.StartDate)
        {
            throw new InvalidOperationException("End date must be on or after start date.");
        }

        bulletin.Title = request.Title;
        bulletin.Text = request.Text;
        bulletin.Active = request.Active;
        bulletin.StartDate = request.StartDate;
        bulletin.EndDate = request.EndDate;
        bulletin.LebUserId = userId;
        bulletin.Modified = DateTime.UtcNow;

        await _bulletinRepository.SaveChangesAsync(cancellationToken);

        return new BulletinAdminDto
        {
            BulletinId = bulletin.BulletinId,
            Title = bulletin.Title,
            Text = bulletin.Text,
            Active = bulletin.Active,
            StartDate = bulletin.StartDate,
            EndDate = bulletin.EndDate,
            CreateDate = bulletin.CreateDate,
            Modified = bulletin.Modified,
            ModifiedByUsername = null
        };
    }

    public async Task<bool> DeleteBulletinAsync(Guid bulletinId, Guid jobId, CancellationToken cancellationToken = default)
    {
        var bulletin = await _bulletinRepository.GetByIdAsync(bulletinId, cancellationToken);
        if (bulletin == null)
        {
            return false;
        }

        if (bulletin.JobId != jobId)
        {
            throw new InvalidOperationException("Bulletin does not belong to the current job.");
        }

        _bulletinRepository.Remove(bulletin);
        await _bulletinRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> BatchUpdateStatusAsync(Guid jobId, bool active, CancellationToken cancellationToken = default)
    {
        return await _bulletinRepository.BatchUpdateActiveStatusAsync(jobId, active, cancellationToken);
    }

    private static string ReplaceTextTokens(string text, string jobName, string uslaxDate)
    {
        return text
            .Replace(JobNameToken, jobName, StringComparison.OrdinalIgnoreCase)
            .Replace(UslaxDateToken, uslaxDate, StringComparison.OrdinalIgnoreCase);
    }
}
