using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Bulletin;
using TSIC.Contracts.Repositories;
using TSIC.API.Services.Shared.Bulletins.TokenResolution;
using TSIC.API.Services.Shared.Jobs;
using BulletinEntity = TSIC.Domain.Entities.Bulletins;

namespace TSIC.API.Services.Shared.Bulletins;

/// <summary>
/// Service for managing bulletin business logic.
/// Public fetch path: text-token substitution (!JOBNAME etc.) + !TOKEN resolution via BulletinTokenRegistry.
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

    // Go-live cutover switch (appsettings "bGoLive"): false in Production, true elsewhere.
    // When true, legacy-link bulletins are auto-retired on read (smart bulletins have
    // superseded them). Distinct from IHostEnvironment.IsSandbox() on purpose — this flag
    // is meant to be flipped to true in Production at cutover; env identity can't express that.
    private readonly bool _bGoLive;

    public BulletinService(
        IJobLookupService jobLookupService,
        IJobRepository jobRepository,
        IBulletinRepository bulletinRepository,
        BulletinTokenRegistry tokenRegistry,
        IConfiguration configuration,
        ILogger<BulletinService> logger)
    {
        _jobLookupService = jobLookupService;
        _jobRepository = jobRepository;
        _bulletinRepository = bulletinRepository;
        _tokenRegistry = tokenRegistry;
        _bGoLive = configuration.GetValue<bool>("bGoLive");
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

        var jobName = jobMetadata.JobName;
        var uslaxDate = jobMetadata.USLaxNumberValidThroughDate?.ToString("M/d/yy") ?? string.Empty;

        // Build token context once; reused across all bulletins in this response.
        // Bulletins are public-facing, so NO viewer identity is part of the context.
        var pulse = await _jobRepository.GetJobPulseAsync(jobPath, cancellationToken);
        TokenContext? tokenCtx = null;
        if (pulse != null)
        {
            tokenCtx = new TokenContext
            {
                JobPath = jobPath,
                Job = new TokenJobInfo
                {
                    JobName = jobMetadata.JobName,
                    USLaxNumberValidThroughDate = jobMetadata.USLaxNumberValidThroughDate
                },
                Pulse = pulse
            };
        }

        var processedBulletins = new List<BulletinDto>();
        List<Guid>? legacyBulletinIds = null;
        foreach (var bulletin in bulletins)
        {
            // Go-live cutover: smart bulletins have superseded the hand-authored bulletins
            // whose links point at legacy MVC routes. In go-live environments (bGoLive=true)
            // retire those (Active = 0) and drop them from the response; Production (false)
            // is untouched. Detection mirrors the frontend TranslateLegacyUrlsPipe — see
            // LegacyBulletinPatterns. Runs on the raw body, before !TOKEN resolution (which
            // emits new-route links and would never match a legacy fragment).
            if (_bGoLive && LegacyBulletinPatterns.HasLegacyLink(bulletin.Text))
            {
                (legacyBulletinIds ??= new List<Guid>()).Add(bulletin.BulletinId);
                continue;
            }

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

        // Lazy one-time retirement: after the first fetch flips them, the repository's
        // Active filter excludes them on every subsequent fetch. Single atomic UPDATE.
        if (legacyBulletinIds is { Count: > 0 })
        {
            var retired = await _bulletinRepository.DeactivateBulletinsAsync(
                jobMetadata.JobId, legacyBulletinIds, cancellationToken);
            _logger.LogInformation(
                "Retired {Count} legacy-link bulletin(s) for job {JobPath} (bGoLive).",
                retired, jobPath);
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
            CreateDate = DateTime.Now,
            Modified = DateTime.Now
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
        bulletin.Modified = DateTime.Now;

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

    public Task<bool> DeactivateBulletinAsync(Guid bulletinId, Guid jobId, CancellationToken cancellationToken = default)
        => SetBulletinActiveAsync(bulletinId, jobId, false, cancellationToken);

    public async Task<bool> SetBulletinActiveAsync(Guid bulletinId, Guid jobId, bool active, CancellationToken cancellationToken = default)
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

        if (bulletin.Active != active)
        {
            bulletin.Active = active;
            bulletin.Modified = DateTime.Now;
            await _bulletinRepository.SaveChangesAsync(cancellationToken);
        }

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
