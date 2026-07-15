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
        // Exact profile-type match, segment-aware for pipe-delimited CoreRegformPlayer values.
        // A plain StartsWith(profileType) would let PP10 also match PP100/PP101 once clone names
        // pass 99 (new names are unpadded), silently overwriting those jobs on the write paths. CR-107.
        return await _context.Jobs
            .Where(j => j.CoreRegformPlayer != null && (
                j.CoreRegformPlayer == profileType
                || j.CoreRegformPlayer.StartsWith(profileType + "|")
                || j.CoreRegformPlayer.EndsWith("|" + profileType)
                || j.CoreRegformPlayer.Contains("|" + profileType + "|")))
            .ToListAsync();
    }

    public async Task<JobBasicInfo?> GetJobBasicInfoAsync(Guid jobId)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new JobBasicInfo
            {
                JobName = j.JobName ?? "",
                CustomerName = string.Empty,
                CoreRegformPlayer = j.RegformNamePlayer ?? string.Empty,
                PlayerProfileMetadataJson = j.PlayerProfileMetadataJson ?? string.Empty,
                AdultProfileMetadataJson = j.AdultProfileMetadataJson ?? string.Empty
            })
            .SingleOrDefaultAsync();
    }

    public async Task<JobFormSnapshot?> GetJobFormSnapshotAsync(Guid jobId)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new JobFormSnapshot
            {
                JobId = j.JobId,
                JobName = j.JobName ?? string.Empty,
                Year = j.Year,
                CoreRegformPlayer = j.CoreRegformPlayer,
                JsonOptions = j.JsonOptions,
                PlayerProfileMetadataJson = j.PlayerProfileMetadataJson,
                AdultProfileMetadataJson = j.AdultProfileMetadataJson
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
                JobName = j.JobName ?? string.Empty,
                CustomerName = string.Empty,
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
                JobName = j.JobName ?? "",
                CoreRegformPlayer = j.CoreRegformPlayer ?? string.Empty,
                PlayerProfileMetadataJson = j.PlayerProfileMetadataJson ?? string.Empty
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
        // Exact profile-type match, segment-aware for pipe-delimited CoreRegformPlayer values.
        // Same collision as GetJobsByProfileTypeAsync: a plain StartsWith(profileType) would let
        // PP10 also match PP100/PP101 once clone names pass 99, returning the wrong job's
        // metadata on this read path. CR-107.
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.CoreRegformPlayer != null && (
                    j.CoreRegformPlayer == profileType
                    || j.CoreRegformPlayer.StartsWith(profileType + "|")
                    || j.CoreRegformPlayer.EndsWith("|" + profileType)
                    || j.CoreRegformPlayer.Contains("|" + profileType + "|"))
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
            .Select(j => j.PlayerProfileMetadataJson!)
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
            .Select(j => j.CoreRegformPlayer!)
            .ToListAsync();
    }

    // ============ ADULT PROFILE READ OPERATIONS ============

    public async Task<List<JobForAdultProfileSummary>> GetJobsForAdultProfileSummaryAsync()
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => !string.IsNullOrEmpty(j.RegformNameCoach))
            .Select(j => new JobForAdultProfileSummary
            {
                JobId = j.JobId,
                JobName = j.JobName ?? "",
                Year = j.Year,
                RegformNameCoach = j.RegformNameCoach,
                AdultProfileMetadataJson = j.AdultProfileMetadataJson
            })
            .ToListAsync();
    }

    public async Task<List<Jobs>> GetJobsForAdultMigrationAsync()
    {
        return await _context.Jobs
            .Where(j => !string.IsNullOrEmpty(j.RegformNameCoach))
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

    public async Task UpdateJobCoreRegformPlayerByJobIdAsync(Guid jobId, string coreRegformPlayer)
    {
        var job = await _context.Jobs.FindAsync(jobId);
        if (job != null)
        {
            job.CoreRegformPlayer = coreRegformPlayer;
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

    public async Task UpdateJobAdultMetadataAsync(Guid jobId, string adultMetadataJson)
    {
        var job = await _context.Jobs.FindAsync(jobId);
        if (job != null)
        {
            job.AdultProfileMetadataJson = adultMetadataJson;
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateMultipleJobsAdultMetadataAsync(List<Jobs> jobs)
    {
        // Jobs are already tracked entities from GetJobsForAdultMigrationAsync; the caller mutated
        // AdultProfileMetadataJson (and JsonOptions for apparel). Just persist.
        await _context.SaveChangesAsync();
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
                PlayerProfileMetadataJson = r.Job.PlayerProfileMetadataJson,
                AdultProfileMetadataJson = r.Job.AdultProfileMetadataJson
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
