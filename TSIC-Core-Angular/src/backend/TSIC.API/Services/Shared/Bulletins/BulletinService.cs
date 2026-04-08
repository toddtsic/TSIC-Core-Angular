using System.Text.RegularExpressions;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Bulletin;
using TSIC.Contracts.Repositories;
using TSIC.API.Services.Shared.Jobs;
using BulletinEntity = TSIC.Domain.Entities.Bulletins;

namespace TSIC.API.Services.Shared.Bulletins;

/// <summary>
/// Service for managing bulletin business logic including URL translation and token substitution.
/// </summary>
public partial class BulletinService : IBulletinService
{
    // Token constants for text substitution
    private const string JobNameToken = "!JOBNAME";
    private const string UslaxDateToken = "!USLAXVALIDTHROUGHDATE";

    // Legacy URL patterns for registration wizards
    // Matches: /JSEG/StartARegistration/Index?bPlayer=true... or full domain URLs
    [GeneratedRegex(
        @"(?:https?://[^/""']*)?/[^/""']+/StartARegistration/Index\?bPlayer=true[^""']*",
        RegexOptions.IgnoreCase)]
    private static partial Regex PlayerRegistrationUrlPattern();

    [GeneratedRegex(
        @"(?:https?://[^/""']*)?/[^/""']+/StartARegistration/Index\?bPlayer=false&bClubRep=true[^""']*",
        RegexOptions.IgnoreCase)]
    private static partial Regex TeamRegistrationUrlPattern();

    // Matches: /JSEG/StartARegistration/Index?...bStaff=true... (coach/staff registration)
    [GeneratedRegex(
        @"(?:https?://[^/""']*)?/[^/""']+/StartARegistration/Index\?[^""']*bStaff=true[^""']*",
        RegexOptions.IgnoreCase)]
    private static partial Regex StaffRegistrationUrlPattern();

    // Matches: /JSEG/Rosters/RostersPublicLookupTourny (public tournament rosters)
    [GeneratedRegex(
        @"(?:https?://[^/""']*)?/[^/""']+/Rosters/RostersPublicLookupTourny[^""']*",
        RegexOptions.IgnoreCase)]
    private static partial Regex PublicRosterUrlPattern();

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

        // Process bulletin title and text with token replacement + legacy URL translation
        var jobName = jobMetadata.JobName;
        var uslaxDate = jobMetadata.USLaxNumberValidThroughDate?.ToString("M/d/yy") ?? string.Empty;
        var playerRegUrl = $"/{jobPath}/registration/player";
        var teamRegUrl = $"/{jobPath}/registration/team";
        var staffRegUrl = $"/{jobPath}/registration/adult";
        var publicRosterUrl = $"/{jobPath}/rosters";

        var processedBulletins = new List<BulletinDto>();
        foreach (var bulletin in bulletins)
        {
            var processedTitle = ReplaceTokens(bulletin.Title ?? string.Empty, jobName, uslaxDate);
            var processedText = ReplaceTokens(bulletin.Text ?? string.Empty, jobName, uslaxDate);

            // Translate legacy URLs to Angular routes
            // More specific patterns first: team (bClubRep=true), staff (bStaff=true), then player (bPlayer=true)
            processedText = TeamRegistrationUrlPattern().Replace(processedText, teamRegUrl);
            processedText = StaffRegistrationUrlPattern().Replace(processedText, staffRegUrl);
            processedText = PlayerRegistrationUrlPattern().Replace(processedText, playerRegUrl);
            processedText = PublicRosterUrlPattern().Replace(processedText, publicRosterUrl);

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

    private static string ReplaceTokens(string text, string jobName, string uslaxDate)
    {
        return text
            .Replace(JobNameToken, jobName, StringComparison.OrdinalIgnoreCase)
            .Replace(UslaxDateToken, uslaxDate, StringComparison.OrdinalIgnoreCase);
    }
}
