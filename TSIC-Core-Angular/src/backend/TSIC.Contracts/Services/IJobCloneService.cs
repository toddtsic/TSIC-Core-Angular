using TSIC.Contracts.Dtos.JobClone;

namespace TSIC.Contracts.Services;

public interface IJobCloneService
{
    /// <summary>
    /// Clone a source job into a new job. COPY-EVERYTHING philosophy: every scalar field
    /// of every cloned table copies mechanically; exceptions live in the versioned reset
    /// rules (JobCloneResetRules). The clone re-plans INSIDE its transaction; when the
    /// request carries a PlanFingerprint and source data moved since preview, it throws
    /// ClonePlanChangedException carrying the fresh plan (controller → 409).
    ///
    /// Safe-by-default on every clone: BSuspendPublic=true; all five BRegistrationAllow*
    /// false; mobile/schedule/roster exposure off; Director+SuperDirector regs inactive;
    /// processing rates floored at the current new-job rate; bulletins inactive.
    ///
    /// authorCustomerId (optional): same-customer guard; null skips (SuperUser-only
    /// controller — target always inherits source.CustomerId).
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
    /// Build the clone PLAN without committing — per-step counts, eligibility breakdown,
    /// warnings, resolved rates, date shifts, fingerprint. The workbench renders this
    /// continuously; the same planner runs inside the clone transaction, so preview and
    /// clone cannot diverge. actorUserId feeds the +1-actor-registration count.
    /// </summary>
    Task<ClonePlanDto> PreviewCloneAsync(
        JobCloneRequest request,
        string actorUserId,
        Guid? authorCustomerId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Flip Jobs.BSuspendPublic = false on the target job (release site to public).
    /// </summary>
    Task<ReleaseResponse> ReleaseSiteAsync(
        Guid jobId,
        string actorUserId,
        Guid? authorCustomerId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Flip Registrations.BActive = true on the given registration IDs — scoped to the
    /// target job. Any registrationId NOT belonging to the target job is rejected with 403.
    /// </summary>
    Task<ReleaseResponse> ReleaseAdminsAsync(
        Guid jobId,
        IList<Guid> registrationIds,
        string actorUserId,
        Guid? authorCustomerId = null,
        CancellationToken ct = default);

    /// <summary>
    /// List all admin registrations (Superuser/Director/SuperDirector) on a job.
    /// Populates the Release page's admin-activation panel.
    /// </summary>
    Task<List<ReleasableAdminDto>> GetReleasableAdminsAsync(
        Guid jobId,
        CancellationToken ct = default);

    /// <summary>True when a Job with the given jobPath already exists (inline uniqueness check).</summary>
    Task<bool> JobPathExistsAsync(string jobPath, CancellationToken ct = default);

    /// <summary>True when a Job with the given jobName already exists (inline uniqueness check).</summary>
    Task<bool> JobNameExistsAsync(string jobName, CancellationToken ct = default);

    /// <summary>
    /// Type-aware verify-then-release checklist (the modern JobCloneQA): the job's LIVE
    /// settings grouped by section, ordered by job-type relevance, with configure
    /// deep-links. Release panel 1.
    /// </summary>
    Task<JobVerifyChecklistDto> GetVerifyChecklistAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Open registration for the chosen personas (release panel 4). Additive: flips the
    /// requested BRegistrationAllow* flags ON (they start false on every clone); closing
    /// lives in job settings.
    /// </summary>
    Task<RegistrationFlagsDto> OpenRegistrationAsync(
        Guid jobId,
        OpenRegistrationRequest request,
        string actorUserId,
        Guid? authorCustomerId = null,
        CancellationToken ct = default);

    // ── Dev-only undo (controller enforces sandbox + SuperUser policy) ──

    /// <summary>
    /// Returns whether a freshly-cloned job can be safely deleted from dev DB, with row
    /// counts for the confirm modal.
    /// </summary>
    Task<DevUndoStatusResponse> GetDevUndoStatusAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Cascade-deletes a freshly-cloned Job (and all entities the clone created) from dev
    /// DB. Re-runs predicate checks inside the same transaction (TOCTOU defense). Cloned
    /// Leagues rows are removed only when exclusively owned by this job.
    /// </summary>
    Task DeleteClonedJobAsync(Guid jobId, CancellationToken ct = default);
}
