using TSIC.Contracts.Dtos.JobClone;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Source-team projection used for LADT scope. Carries the full Teams entity plus the joined
/// AgegroupName (status tokens like WAITLIST/DROPPED live encoded in this string) so the
/// service can apply both the paid filter and the status filter without a second query.
/// </summary>
public record TeamCloneSource
{
    public required Teams Team { get; init; }
    public required string? AgegroupName { get; init; }
}

public interface IJobCloneRepository
{
    // ── Source data loading (AsNoTracking) ──
    Task<Jobs?> GetSourceJobAsync(Guid jobId, CancellationToken ct = default);
    Task<JobDisplayOptions?> GetSourceDisplayOptionsAsync(Guid jobId, CancellationToken ct = default);
    Task<JobOwlImages?> GetSourceOwlImagesAsync(Guid jobId, CancellationToken ct = default);
    Task<List<Bulletins>> GetSourceBulletinsAsync(Guid jobId, CancellationToken ct = default);
    Task<List<JobAgeRanges>> GetSourceAgeRangesAsync(Guid jobId, CancellationToken ct = default);
    Task<List<JobMenus>> GetSourceMenusWithItemsAsync(Guid jobId, CancellationToken ct = default);
    Task<List<JobReports>> GetSourceJobReportsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Per-job nav overrides (Nav.JobId == jobId). The default nav (JobId IS NULL) is shared
    /// across jobs and is NOT cloned. NavItems eager-loaded.
    /// </summary>
    Task<List<Nav>> GetSourceNavWithItemsAsync(Guid jobId, CancellationToken ct = default);
    Task<List<Registrations>> GetSourceAdminRegistrationsAsync(Guid jobId, CancellationToken ct = default);
    Task<Leagues?> GetSourceLeagueAsync(Guid jobId, CancellationToken ct = default);
    Task<JobLeagues?> GetSourceJobLeagueAsync(Guid jobId, Guid leagueId, CancellationToken ct = default);
    Task<List<Agegroups>> GetSourceAgegroupsAsync(Guid leagueId, string? season, CancellationToken ct = default);
    Task<List<Divisions>> GetSourceDivisionsAsync(List<Guid> agegroupIds, CancellationToken ct = default);

    /// <summary>
    /// Source teams classified for LADT cloning. Each row carries the team + its agegroup name
    /// (so the service can apply the WAITLIST/DROPPED filter without a second query) and a flag
    /// indicating whether the team is "ClubRep-paid" (Teams.ClubrepRegistrationid IS NOT NULL).
    /// Caller filters out paid + waitlist/dropped before cloning.
    /// </summary>
    Task<List<TeamCloneSource>> GetSourceTeamsAsync(Guid jobId, CancellationToken ct = default);

    // ── Validation ──
    Task<bool> JobPathExistsAsync(string jobPath, CancellationToken ct = default);
    Task<bool> JobNameExistsAsync(string jobName, CancellationToken ct = default);

    // ── Source picker list ──
    Task<List<JobCloneSourceDto>> GetCloneableJobsAsync(CancellationToken ct = default);

    // ── Write operations (queue in change tracker) ──
    void AddJob(Jobs job);
    void AddDisplayOptions(JobDisplayOptions options);
    void AddOwlImages(JobOwlImages images);
    void AddBulletins(IEnumerable<Bulletins> bulletins);
    void AddAgeRanges(IEnumerable<JobAgeRanges> ranges);
    void AddMenu(JobMenus menu);
    void AddMenuItems(IEnumerable<JobMenuItems> items);
    void AddJobReports(IEnumerable<JobReports> reports);

    /// <summary>
    /// Adds a Nav root with its NavItem children attached via the navigation property.
    /// EF resolves identity-int FKs (NavId, ParentNavItemId) at SaveChanges time.
    /// </summary>
    void AddNav(Nav nav);
    void AddRegistrations(IEnumerable<Registrations> registrations);
    void AddLeague(Leagues league);
    void AddJobLeague(JobLeagues jobLeague);
    void AddAgegroups(IEnumerable<Agegroups> agegroups);
    void AddDivisions(IEnumerable<Divisions> divisions);
    void AddTeams(IEnumerable<Teams> teams);

    // ── Transaction + commit ──
    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task RollbackTransactionAsync(CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    // ── Release ops ──
    /// <summary>Returns the tracked Jobs entity for mutation (release-site toggle).</summary>
    Task<Jobs?> GetJobForUpdateAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Returns tracked Registrations for the given job + regIds (for activation).</summary>
    Task<List<Registrations>> GetRegistrationsForUpdateAsync(Guid jobId, IList<Guid> registrationIds, CancellationToken ct = default);

    /// <summary>
    /// Returns admin registrations for a job with person info joined.
    /// Used by the Release screen's activation panel.
    /// </summary>
    Task<List<ReleasableAdminDto>> GetReleasableAdminsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Returns jobs currently BSuspendPublic=true. When customerId is supplied, filters to that customer.
    /// </summary>
    Task<List<SuspendedJobDto>> GetSuspendedJobsAsync(Guid? customerId, CancellationToken ct = default);

    // ── Dev-only undo (cascade delete a freshly-cloned job) ──

    /// <summary>
    /// Counts rows across every table that references the given Job. Used by the dev-undo
    /// flow to gate the delete (all "user-touched" indicators must be 0) and to populate
    /// the confirm modal with row-by-table breakdown.
    /// </summary>
    Task<DevUndoCounts> GetDevUndoCountsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Returns true if the cloned league is referenced by ONLY this job's JobLeagues row
    /// — guards against a future change that might link an existing Leagues to multiple
    /// jobs. If false, the dev-undo skips deleting the Leagues row.
    /// </summary>
    Task<bool> IsLeagueExclusivelyOwnedByJobAsync(Guid jobId, Guid leagueId, CancellationToken ct = default);

    /// <summary>
    /// Loads the JobLeagues row for the job (single primary, if any) so the service can
    /// resolve the cloned LeagueId for cleanup.
    /// </summary>
    Task<JobLeagues?> GetJobLeagueForJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Cascade-deletes a Jobs row + every entity created during clone. Safety predicates
    /// MUST be checked by the service inside the same transaction before this is called;
    /// this method assumes deletion is authorized.
    /// </summary>
    Task CascadeDeleteJobAsync(Guid jobId, Guid? clonedLeagueId, CancellationToken ct = default);
}
