using TSIC.Contracts.Dtos.JobClone;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Source-team projection used for LADT scope. Carries the full Teams entity plus the
/// joined AgegroupName (status tokens like WAITLIST/Dropped live encoded in this string)
/// and the owning club rep's ClubName — the structure-vs-competing eligibility signal:
/// an owned team whose owner's club_name is EMPTY is a director-created house team
/// (structure) and clones; an owned team with a real club name is a competing team and
/// never clones.
/// </summary>
public record TeamCloneSource
{
    public required Teams Team { get; init; }
    public required string? AgegroupName { get; init; }
    public required string? OwnerClubName { get; init; }
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

    /// <summary>
    /// ALL JobLeagues rows for the job with League eager-loaded, primary first (T3 —
    /// every league clones; nothing is silently dropped).
    /// </summary>
    Task<List<JobLeagues>> GetSourceJobLeaguesAsync(Guid jobId, CancellationToken ct = default);
    Task<List<Agegroups>> GetSourceAgegroupsAsync(Guid leagueId, string? season, CancellationToken ct = default);
    Task<List<Divisions>> GetSourceDivisionsAsync(List<Guid> agegroupIds, CancellationToken ct = default);

    /// <summary>
    /// Source teams with the classification signals (agegroup name + owner's club name)
    /// joined in. Eligibility itself is decided by JobClonePlanner.
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

    // ── Target-customer probes (cross-customer clone warnings) ──

    /// <summary>Customer display name, or null when the id matches no customer.</summary>
    Task<string?> GetCustomerNameAsync(Guid customerId, CancellationToken ct = default);

    /// <summary>
    /// True when the customer has both Authorize.Net credentials populated. Returns a BOOLEAN
    /// deliberately — the plan warns that a target customer can't take card payments without
    /// ever moving key material out of the repository.
    /// </summary>
    Task<bool> CustomerHasAdnCredentialsAsync(Guid customerId, CancellationToken ct = default);

    // ── Dev-only undo (cascade delete a freshly-cloned job) ──

    /// <summary>
    /// Counts rows across every table that references the given Job. The ancillary sum is
    /// generated from an EF-model walk (every entity with a JobId property that is NOT
    /// part of the clone manifest) — a new job-scoped table is counted automatically, so
    /// dev-undo can never delete through a table it doesn't know about.
    /// </summary>
    Task<DevUndoCounts> GetDevUndoCountsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Returns true if the cloned league is referenced by ONLY this job's JobLeagues row.
    /// If false, the dev-undo skips deleting that Leagues row.
    /// </summary>
    Task<bool> IsLeagueExclusivelyOwnedByJobAsync(Guid jobId, Guid leagueId, CancellationToken ct = default);

    /// <summary>
    /// Loads ALL JobLeagues rows for the job (tracked) so the service can resolve every
    /// cloned LeagueId for cleanup (T3 multi-league).
    /// </summary>
    Task<List<JobLeagues>> GetJobLeaguesForJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Cascade-deletes a Jobs row + every entity created during clone, iterating the
    /// JobCloneStepOrder manifest in REVERSE. Safety predicates MUST be checked by the
    /// service inside the same transaction before this is called.
    /// </summary>
    Task CascadeDeleteJobAsync(Guid jobId, IReadOnlyList<Guid> clonedLeagueIds, CancellationToken ct = default);
}
