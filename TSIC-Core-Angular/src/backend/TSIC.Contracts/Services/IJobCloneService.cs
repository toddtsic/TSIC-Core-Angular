using TSIC.Contracts.Dtos.JobClone;

namespace TSIC.Contracts.Services;

public interface IJobCloneService
{
    /// <summary>
    /// Clone a source job into a new job with the given parameters.
    /// Clones: Job, DisplayOptions, OwlImages, Bulletins, AgeRanges,
    /// Menus (with items), Admin Registrations, League, Agegroups, Divisions, Fees.
    ///
    /// Safe-by-default state on every clone:
    ///   BSuspendPublic=true; Director+SuperDirector regs BActive=false (Superuser unchanged);
    ///   BClubRepAllowEdit/Delete/Add=true; ProcessingFeePercent reset to current minimum.
    ///
    /// Date-sensitive fields shift by year-delta (Bulletins, FeeModifier windows,
    /// Agegroup DOB + discount/late-fee windows when UpAgegroupNamesByOne is true,
    /// Jobs EventStart/End, AdnArbstartDate).
    ///
    /// authorCustomerId (optional): when supplied, enforces same-customer guard
    /// (source.CustomerId must equal authorCustomerId or the call throws).
    /// Null skips the guard (today's SuperUser-only controller passes null — target
    /// always inherits source.CustomerId so cross-customer attempts are impossible).
    /// </summary>
    Task<JobCloneResponse> CloneJobAsync(
        JobCloneRequest request,
        string superUserId,
        Guid? authorCustomerId = null,
        CancellationToken ct = default);

    /// <summary>
    /// List all jobs available as clone sources (for the frontend picker).
    /// </summary>
    Task<List<JobCloneSourceDto>> GetCloneableJobsAsync(CancellationToken ct = default);

    /// <summary>
    /// Create a new empty job (no source) for new-customer onboarding.
    /// Lands with the same safe-by-default state as a clone: BSuspendPublic=true,
    /// BClubRepAllowEdit/Delete/Add=true, ProcessingFeePercent=current floor.
    /// Author's own admin Registration is created with BActive=true.
    ///
    /// authorCustomerId: when supplied, enforces same-customer guard
    /// (request.CustomerId must equal authorCustomerId). SuperUser passes null to bypass.
    /// </summary>
    Task<BlankJobResponse> CreateBlankJobAsync(
        BlankJobRequest request,
        string authorUserId,
        Guid? authorCustomerId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Preview the transforms a clone will perform — year-delta shifts, name inference,
    /// admin deactivation counts — without committing. Used by the wizard's Step 3
    /// preview pane.
    ///
    /// authorCustomerId: same guard semantics as CloneJobAsync.
    /// </summary>
    Task<JobClonePreviewResponse> PreviewCloneAsync(
        JobCloneRequest request,
        Guid? authorCustomerId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Flip Jobs.BSuspendPublic = false on the target job (release site to public).
    /// authorCustomerId, if supplied, must equal the job's CustomerId.
    /// </summary>
    Task<ReleaseResponse> ReleaseSiteAsync(
        Guid jobId,
        string actorUserId,
        Guid? authorCustomerId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Flip Registrations.BActive = true on the given registration IDs — scoped to the target job.
    /// Any registrationId NOT belonging to the target job is rejected with 403.
    /// authorCustomerId, if supplied, must equal the job's CustomerId.
    /// </summary>
    Task<ReleaseResponse> ReleaseAdminsAsync(
        Guid jobId,
        IList<Guid> registrationIds,
        string actorUserId,
        Guid? authorCustomerId = null,
        CancellationToken ct = default);

    /// <summary>
    /// List all admin registrations (Superuser/Director/SuperDirector) on a job, with person info.
    /// Used to populate the Release screen's admin-activation panel.
    /// </summary>
    Task<List<ReleasableAdminDto>> GetReleasableAdminsAsync(
        Guid jobId,
        CancellationToken ct = default);

    /// <summary>
    /// List jobs currently in the suspended state (BSuspendPublic = true). Used by the Landing
    /// screen to show a "ready to release" list. authorCustomerId filters to that customer's jobs.
    /// </summary>
    Task<List<SuspendedJobDto>> GetSuspendedJobsAsync(
        Guid? authorCustomerId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns true when a Job with the given jobPath already exists. Used by the Step 2→3
    /// uniqueness check so authors aren't surprised at Submit.
    /// </summary>
    Task<bool> JobPathExistsAsync(string jobPath, CancellationToken ct = default);

    /// <summary>
    /// Returns true when a Job with the given jobName already exists. Used by the Step 2→3
    /// uniqueness check alongside JobPathExistsAsync.
    /// </summary>
    Task<bool> JobNameExistsAsync(string jobName, CancellationToken ct = default);
}
