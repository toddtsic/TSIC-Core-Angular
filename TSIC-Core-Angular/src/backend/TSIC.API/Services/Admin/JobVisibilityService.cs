using TSIC.Contracts.Dtos.JobConfig;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Admin;

/// <summary>
/// SuperUser "Quick Links" editor service. Reads/writes the handful of Jobs.Jobs
/// flags the public landing hero grounds its CTA cards on — a focused alternative
/// to hunting the same flags across Configure Job's logical tabs. Stores no
/// separate state; the flag IS the single source of truth. Partial updates let
/// the UI save one toggle at a time.
/// </summary>
public class JobVisibilityService : IJobVisibilityService
{
    private readonly IJobConfigRepository _repo;

    public JobVisibilityService(IJobConfigRepository repo) => _repo = repo;

    public async Task<JobVisibilityDto> GetAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _repo.GetJobByIdAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        return new JobVisibilityDto
        {
            AllowPlayerRegistration = job.BRegistrationAllowPlayer ?? false,
            AllowTeamRegistration = job.BRegistrationAllowTeam ?? false,
            PublishSchedule = job.BScheduleAllowPublicAccess ?? false,
            ShowPublicRosters = job.BAllowRosterViewPlayer,
            EnableStore = job.BEnableStore ?? false,
            OfferPlayerInsurance = job.BOfferPlayerRegsaverInsurance ?? false,
        };
    }

    public async Task UpdateAsync(Guid jobId, UpdateJobVisibilityRequest req, CancellationToken ct = default)
    {
        var job = await _repo.GetJobTrackedAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        // Only non-null flags are applied — supports per-toggle save-on-change.
        if (req.AllowPlayerRegistration.HasValue) job.BRegistrationAllowPlayer = req.AllowPlayerRegistration.Value;
        if (req.AllowTeamRegistration.HasValue) job.BRegistrationAllowTeam = req.AllowTeamRegistration.Value;
        if (req.PublishSchedule.HasValue) job.BScheduleAllowPublicAccess = req.PublishSchedule.Value;
        if (req.ShowPublicRosters.HasValue) job.BAllowRosterViewPlayer = req.ShowPublicRosters.Value;
        if (req.EnableStore.HasValue) job.BEnableStore = req.EnableStore.Value;
        if (req.OfferPlayerInsurance.HasValue) job.BOfferPlayerRegsaverInsurance = req.OfferPlayerInsurance.Value;

        job.Modified = DateTime.Now;
        await _repo.SaveChangesAsync(ct);
    }
}
