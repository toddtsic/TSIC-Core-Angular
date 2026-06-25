using TSIC.Contracts.Dtos.JobConfig;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

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

        // Registration relevance is fee-driven: a reg type is only meaningful when its
        // role's fees exist (Player → player fees, ClubRep → team fees). Sequential
        // awaits — the repo shares one scoped DbContext (no Task.WhenAll).
        var playerFeesConfigured = await _repo.JobHasFeesForRoleAsync(jobId, RoleConstants.Player, ct);
        var teamFeesConfigured = await _repo.JobHasFeesForRoleAsync(jobId, RoleConstants.ClubRep, ct);
        // Coach/staff registration relevance is team-driven (a coach requests a team),
        // not fee-driven — coaches are unpaid (FeeBase=0, no JobFees role).
        var teamsConfigured = await _repo.JobHasTeamsAsync(jobId, ct);

        return new JobVisibilityDto
        {
            AllowPlayerRegistration = job.BRegistrationAllowPlayer ?? false,
            AllowTeamRegistration = job.BRegistrationAllowTeam ?? false,
            PublishSchedule = job.BScheduleAllowPublicAccess ?? false,
            EnableStore = job.BEnableStore ?? false,
            OfferPlayerInsurance = job.BOfferPlayerRegsaverInsurance ?? false,
            OfferTeamInsurance = job.BOfferTeamRegsaverInsurance ?? false,
            // Public rosters gate ONLY on bRestrictPublicRosters (BAllowRosterViewPlayer
            // governs a logged-in player's OWN roster, not the public page). Inverted:
            // "show public rosters" = NOT restricted.
            ShowPublicRosters = !job.BRestrictPublicRosters,
            AllowStaffRegistration = job.BRegistrationAllowStaff ?? false,
            AllowRefereeRegistration = job.BRegistrationAllowReferee ?? false,
            AllowRecruiterRegistration = job.BRegistrationAllowRecruiter ?? false,
            TeamsConfigured = teamsConfigured,
            PlayerFeesConfigured = playerFeesConfigured,
            TeamFeesConfigured = teamFeesConfigured,
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
        if (req.ShowPublicRosters.HasValue) job.BRestrictPublicRosters = !req.ShowPublicRosters.Value;
        if (req.EnableStore.HasValue) job.BEnableStore = req.EnableStore.Value;
        if (req.OfferPlayerInsurance.HasValue) job.BOfferPlayerRegsaverInsurance = req.OfferPlayerInsurance.Value;
        if (req.OfferTeamInsurance.HasValue) job.BOfferTeamRegsaverInsurance = req.OfferTeamInsurance.Value;
        if (req.AllowStaffRegistration.HasValue) job.BRegistrationAllowStaff = req.AllowStaffRegistration.Value;
        if (req.AllowRefereeRegistration.HasValue) job.BRegistrationAllowReferee = req.AllowRefereeRegistration.Value;
        if (req.AllowRecruiterRegistration.HasValue) job.BRegistrationAllowRecruiter = req.AllowRecruiterRegistration.Value;

        job.Modified = DateTime.Now;
        await _repo.SaveChangesAsync(ct);
    }
}
