using Microsoft.EntityFrameworkCore;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.API.Services;

public interface IJobLookupService
{
    Task<Guid?> GetJobIdByPathAsync(string jobPath);
    Task<bool> IsPlayerRegistrationActiveAsync(Guid jobId);
    Task<JobMetadataDto?> GetJobMetadataAsync(string jobPath);
}

public class JobMetadataDto
{
    public Guid JobId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public string JobPath { get; set; } = string.Empty;
    public string? JobLogoPath { get; set; }
    public string? JobBannerPath { get; set; }
    public bool? CoreRegformPlayer { get; set; }
    public DateTime? USLaxNumberValidThroughDate { get; set; }
    public DateTime? ExpiryUsers { get; set; }
    public string? PlayerProfileMetadataJson { get; set; }
    public string? JsonOptions { get; set; }
    public string? MomLabel { get; set; }
    public string? DadLabel { get; set; }
}

public class JobLookupService : IJobLookupService
{
    private readonly SqlDbContext _context;
    private readonly ILogger<JobLookupService> _logger;

    public JobLookupService(SqlDbContext context, ILogger<JobLookupService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Guid?> GetJobIdByPathAsync(string jobPath)
    {
        var job = await _context.Jobs
            .Where(j => j.JobPath == jobPath)
            .Select(j => new { j.JobId })
            .SingleOrDefaultAsync();

        return job?.JobId;
    }

    public async Task<bool> IsPlayerRegistrationActiveAsync(Guid jobId)
    {
        var job = await _context.Jobs
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.BRegistrationAllowPlayer, j.ExpiryUsers })
            .SingleOrDefaultAsync();

        return (job?.BRegistrationAllowPlayer ?? false) && (job.ExpiryUsers > DateTime.Now);
    }

    public async Task<JobMetadataDto?> GetJobMetadataAsync(string jobPath)
    {
        var job = await _context.JobDisplayOptions
            .Where(jdo => jdo.Job.JobPath == jobPath)
            .Select(jdo => new JobMetadataDto
            {
                JobId = jdo.Job.JobId,
                JobName = jdo.Job.JobName ?? string.Empty,
                JobPath = jdo.Job.JobPath ?? string.Empty,
                JobLogoPath = jdo.LogoHeader, // Using BannerFile for logo/banner
                JobBannerPath = jdo.ParallaxSlide1Image,
                CoreRegformPlayer = jdo.Job.CoreRegformPlayer == "1",
                USLaxNumberValidThroughDate = jdo.Job.UslaxNumberValidThroughDate,
                ExpiryUsers = jdo.Job.ExpiryUsers,
                PlayerProfileMetadataJson = jdo.Job.PlayerProfileMetadataJson,
                JsonOptions = jdo.Job.JsonOptions,
                MomLabel = jdo.Job.MomLabel,
                DadLabel = jdo.Job.DadLabel
            })
            .SingleOrDefaultAsync();

        return job;
    }
}
