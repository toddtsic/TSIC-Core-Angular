using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Data access for the fees schema (fees.JobFees + fees.FeeModifiers).
/// All fee configuration reads/writes go through this repository.
/// </summary>
public interface IFeeRepository
{
    /// <summary>
    /// Resolves the effective base fee for a role at a specific team,
    /// cascading Team → Agegroup → Job (most specific non-null wins per field).
    /// Never returns null — returns <see cref="ResolvedFee.NotConfigured"/> when no fee
    /// row exists at any level; check <see cref="ResolvedFee.FeeConfigured"/>.
    /// </summary>
    Task<ResolvedFee?> GetResolvedFeeAsync(
        Guid jobId, string roleId, Guid agegroupId, Guid teamId,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves the effective base fee at the agegroup level (no team override).
    /// Cascades Agegroup → Job.
    /// </summary>
    Task<ResolvedFee?> GetResolvedFeeForAgegroupAsync(
        Guid jobId, string roleId, Guid agegroupId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all active modifiers for a given JobFees row at a point in time.
    /// Active = StartDate &lt;= asOfDate &lt;= EndDate (NULLs treated as unbounded).
    /// </summary>
    Task<List<FeeModifiers>> GetActiveModifiersAsync(
        Guid jobFeeId, DateTime asOfDate,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all active modifiers across all cascade levels (team, agegroup, job)
    /// for a role at a specific team. Modifiers from all levels are stacked.
    /// </summary>
    Task<List<FeeModifiers>> GetActiveModifiersForCascadeAsync(
        Guid jobId, string roleId, Guid agegroupId, Guid teamId, DateTime asOfDate,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves the job-level fee row (AgegroupId IS NULL, TeamId IS NULL).
    /// Used by adult registration where there is no agegroup/team context.
    /// Never returns null — returns <see cref="ResolvedFee.NotConfigured"/> when no fee
    /// is configured at job level for the role; check <see cref="ResolvedFee.FeeConfigured"/>.
    /// </summary>
    Task<ResolvedFee?> GetJobLevelFeeAsync(
        Guid jobId, string roleId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets active modifiers for the job-level fee row only.
    /// Used by adult registration where there is no agegroup/team context.
    /// </summary>
    Task<List<FeeModifiers>> GetActiveModifiersForJobLevelAsync(
        Guid jobId, string roleId, DateTime asOfDate,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all JobFees rows for a job (all roles, all scopes).
    /// Used by LADT fee display.
    /// </summary>
    Task<List<JobFees>> GetJobFeesByJobAsync(
        Guid jobId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all JobFees rows for a specific agegroup (agegroup-level + team-level).
    /// Used by LADT agegroup detail panel.
    /// </summary>
    Task<List<JobFees>> GetJobFeesByAgegroupAsync(
        Guid jobId, Guid agegroupId,
        CancellationToken ct = default);

    /// <summary>
    /// Batch: resolves fees for multiple teams in a single query.
    /// Used by LADT bulk fee recalculation.
    /// Returns resolved fee per teamId.
    /// </summary>
    Task<Dictionary<Guid, ResolvedFee>> GetResolvedFeesByTeamIdsAsync(
        Guid jobId, string roleId, IReadOnlyList<Guid> teamIds,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a tracked JobFees row by its scope (for updates).
    /// Returns null if not found.
    /// </summary>
    Task<JobFees?> GetTrackedByScopeAsync(
        Guid jobId, string roleId, Guid? agegroupId, Guid? teamId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a tracked JobFees row by ID (for updates/deletes).
    /// Includes FeeModifiers.
    /// </summary>
    Task<JobFees?> GetTrackedByIdAsync(Guid jobFeeId, CancellationToken ct = default);

    /// <summary>
    /// Gets all team-scoped JobFees rows (TeamId == teamId) with their FeeModifiers.
    /// AsNoTracking — intended for read-then-clone flows.
    /// </summary>
    Task<List<JobFees>> GetByTeamIdAsync(Guid teamId, CancellationToken ct = default);

    /// <summary>
    /// Gets all agegroup-scoped JobFees rows (AgegroupId == agegroupId AND TeamId IS NULL)
    /// with their FeeModifiers. AsNoTracking — intended for read-then-clone flows.
    /// </summary>
    Task<List<JobFees>> GetByAgegroupScopeAsync(Guid agegroupId, CancellationToken ct = default);

    // Write operations

    /// <summary>Adds a new JobFees row.</summary>
    void Add(JobFees jobFee);

    /// <summary>Adds a new FeeModifiers row.</summary>
    void AddModifier(FeeModifiers modifier);

    /// <summary>Removes a JobFees row (cascade deletes its modifiers).</summary>
    void Remove(JobFees jobFee);

    /// <summary>Removes a FeeModifiers row.</summary>
    void RemoveModifier(FeeModifiers modifier);

    /// <summary>
    /// Batch-deletes every JobFees row scoped to the given agegroup (and their modifiers).
    /// Uses EF Core <c>ExecuteDeleteAsync</c>; commits immediately, independent of other tracked changes.
    /// Caller must have already confirmed no teams/divisions remain under the agegroup.
    /// </summary>
    Task<int> DeleteByAgegroupIdAsync(Guid agegroupId, CancellationToken ct = default);

    /// <summary>Persists all pending changes.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>
/// Result of fee cascade resolution. Contains the effective Deposit and BalanceDue
/// after evaluating Team → Agegroup → Job levels.
/// </summary>
public record ResolvedFee
{
    /// <summary>
    /// True when a JobFees row exists at some cascade level (team/agegroup/job).
    /// False = no fee configured anywhere. This distinguishes a genuinely
    /// <b>unconfigured</b> team (must fail loud — never silently charge $0) from a
    /// legitimately <b>free</b> configured event (FeeConfigured=true, amount $0).
    /// Value-readers can keep using EffectiveBalanceDue (0m either way); new-registration
    /// stamps must check this flag.
    /// </summary>
    public bool FeeConfigured { get; init; }

    /// <summary>Deposit amount (NULL = no deposit, use BalanceDue).</summary>
    public decimal? Deposit { get; init; }

    /// <summary>Balance due amount.</summary>
    public decimal? BalanceDue { get; init; }

    /// <summary>
    /// Effective deposit: Deposit if set, otherwise BalanceDue.
    /// For single-phase jobs, admin only sets BalanceDue and this returns it.
    /// </summary>
    public decimal EffectiveDeposit => Deposit ?? BalanceDue ?? 0m;

    /// <summary>
    /// Effective balance due: BalanceDue if set, otherwise 0.
    /// </summary>
    public decimal EffectiveBalanceDue => BalanceDue ?? 0m;

    /// <summary>Sentinel for "no fee row at any cascade level" (FeeConfigured = false).</summary>
    public static readonly ResolvedFee NotConfigured = new() { FeeConfigured = false };
}
