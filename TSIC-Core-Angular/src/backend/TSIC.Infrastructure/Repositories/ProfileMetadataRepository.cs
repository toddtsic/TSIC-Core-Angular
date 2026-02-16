using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class ProfileMetadataRepository : IProfileMetadataRepository
{
    private readonly SqlDbContext _context;
    private const string CoreRegformExcludeMarker = "PP1_Player_RegForm";

    public ProfileMetadataRepository(SqlDbContext context)
    {
        _context = context;
    }

    // ============ JOBS READ OPERATIONS ============

    public async Task<List<Jobs>> GetJobsWithCoreRegformPlayerAsync()
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => !string.IsNullOrEmpty(j.CoreRegformPlayer) && j.CoreRegformPlayer != CoreRegformExcludeMarker)
            .ToListAsync();
    }

    public async Task<List<Jobs>> GetJobsByProfileTypeAsync(string profileType)
    {
        return await _context.Jobs
            .Where(j => j.CoreRegformPlayer.StartsWith(profileType) || j.CoreRegformPlayer == profileType)
            .ToListAsync();
    }

    public async Task<JobBasicInfo?> GetJobBasicInfoAsync(Guid jobId)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new JobBasicInfo
            {
                JobName = j.JobName,
                CustomerName = null,
                CoreRegformPlayer = j.RegformNamePlayer,
                PlayerProfileMetadataJson = null,
                AdultProfileMetadataJson = null
            })
            .SingleOrDefaultAsync();
    }

    public async Task<JobWithJsonOptions?> GetJobWithJsonOptionsAsync(Guid jobId)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new JobWithJsonOptions
            {
                JobId = j.JobId,
                JobName = j.JobName,
                CustomerName = null,
                JsonOptions = null
            })
            .SingleOrDefaultAsync();
    }

    public async Task<List<JobForProfileSummary>> GetJobsForProfileSummaryAsync()
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => !string.IsNullOrEmpty(j.RegformNamePlayer) && j.RegformNamePlayer != CoreRegformExcludeMarker)
            .Select(j => new JobForProfileSummary
            {
                JobId = j.JobId,
                JobName = j.JobName,
                CoreRegformPlayer = j.CoreRegformPlayer,
                PlayerProfileMetadataJson = j.PlayerProfileMetadataJson
            })
            .ToListAsync();
    }

    public async Task<List<JobKnownProfileType>> GetJobsForKnownProfileTypesAsync()
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => !string.IsNullOrEmpty(j.CoreRegformPlayer)
                && j.CoreRegformPlayer != "0"
                && j.CoreRegformPlayer != "1"
                && j.CoreRegformPlayer != CoreRegformExcludeMarker)
            .Select(j => new JobKnownProfileType
            {
                CoreRegformPlayer = j.CoreRegformPlayer
            })
            .ToListAsync();
    }

    public async Task<JobWithPlayerMetadata?> GetJobWithPlayerMetadataAsync(string profileType)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => (j.CoreRegformPlayer.StartsWith(profileType) || j.CoreRegformPlayer == profileType)
                && j.CoreRegformPlayer != CoreRegformExcludeMarker
                && !string.IsNullOrEmpty(j.PlayerProfileMetadataJson))
            .Select(j => new JobWithPlayerMetadata
            {
                PlayerProfileMetadataJson = j.PlayerProfileMetadataJson
            })
            .FirstOrDefaultAsync();
    }

    public async Task<List<string>> GetAllJobsPlayerMetadataJsonAsync()
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => !string.IsNullOrEmpty(j.PlayerProfileMetadataJson))
            .Select(j => j.PlayerProfileMetadataJson)
            .ToListAsync();
    }

    public async Task<List<Jobs>> GetJobsWithProfileMetadataAsync()
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => !string.IsNullOrEmpty(j.PlayerProfileMetadataJson))
            .ToListAsync();
    }

    public async Task<List<string>> GetJobsCoreRegformValuesAsync()
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => !string.IsNullOrEmpty(j.CoreRegformPlayer) && j.CoreRegformPlayer != "0" && j.CoreRegformPlayer != "1")
            .Select(j => j.CoreRegformPlayer)
            .ToListAsync();
    }

    // ============ JOBS WRITE OPERATIONS ============

    public async Task UpdateJobPlayerMetadataAsync(Guid jobId, string metadataJson)
    {
        var job = await _context.Jobs.FindAsync(jobId);
        if (job != null)
        {
            job.PlayerProfileMetadataJson = metadataJson;
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateMultipleJobsPlayerMetadataAsync(List<Jobs> jobs)
    {
        // Jobs are already tracked entities from GetJobsByProfileTypeAsync
        // Just save changes after caller modifies PlayerProfileMetadataJson
        await _context.SaveChangesAsync();
    }

    public async Task UpdateJobCoreRegformAndMetadataAsync(Guid jobId, string coreRegformPlayer, string metadataJson)
    {
        var job = await _context.Registrations
            .Where(r => r.RegistrationId == jobId)
            .Select(r => r.Job)
            .FirstOrDefaultAsync();

        if (job != null)
        {
            job.CoreRegformPlayer = coreRegformPlayer;
            job.PlayerProfileMetadataJson = metadataJson;
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateJobJsonOptionsAsync(Guid jobId, string jsonOptions)
    {
        var job = await _context.Jobs.FindAsync(jobId);
        if (job != null)
        {
            job.JsonOptions = jsonOptions;
            await _context.SaveChangesAsync();
        }
    }

    // ============ REGISTRATIONS READ OPERATIONS ============

    public async Task<RegistrationJobProjection?> GetJobDataForRegistrationAsync(Guid regId)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.RegistrationId == regId)
            .Select(r => new RegistrationJobProjection
            {
                JobId = r.Job.JobId,
                JobName = r.Job.JobName,
                CoreRegformPlayer = r.Job.CoreRegformPlayer,
                JsonOptions = r.Job.JsonOptions,
                PlayerProfileMetadataJson = r.Job.PlayerProfileMetadataJson
            })
            .FirstOrDefaultAsync();
    }

    public async Task<Guid?> GetRegistrationJobIdAsync(Guid regId)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.RegistrationId == regId)
            .Select(r => r.JobId)
            .FirstOrDefaultAsync();
    }

    public async Task<List<string>> GetDistinctRegistrationColumnValuesAsync(Guid jobId, string columnName)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId)
            .Select(r => EF.Property<string>(r, columnName))
            .Where(val => !string.IsNullOrWhiteSpace(val))
            .Select(val => val.Trim())
            .Distinct()
            .ToListAsync();
    }
}
