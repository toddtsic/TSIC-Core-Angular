using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Data access for the brackets schema. Reads placed bracket games from
/// Leagues.schedule and reads/writes the brackets.* wiring (instances, feeds,
/// seeds). Templates/routes are read-only reference topology (seeded by SQL).
/// </summary>
public interface IBracketRepository
{
    Task<List<PlacedBracketGame>> GetPlacedBracketGamesAsync(
        Guid jobId, Guid agegroupId, Guid divId, CancellationToken ct = default);

    /// <summary>
    /// The bracket template for a strategy (e.g. "SE") + size + variant. The single
    /// strategy switch: drives both generation and projection. Null if unseeded.
    /// </summary>
    Task<Templates?> GetTemplateAsync(
        string strategyCode, int bracketSize, string variant = "Standard", CancellationToken ct = default);

    /// <summary>Single-elimination convenience over <see cref="GetTemplateAsync"/>.</summary>
    Task<Templates?> GetSeTemplateAsync(int bracketSize, CancellationToken ct = default);

    Task<List<TemplateGames>> GetTemplateGamesAsync(int templateId, CancellationToken ct = default);

    Task<List<AdvancementRoutes>> GetTemplateRoutesAsync(int templateId, CancellationToken ct = default);

    Task<BracketInstances?> GetInstanceAsync(
        Guid jobId, Guid agegroupId, Guid divId, CancellationToken ct = default);

    /// <summary>Materialized feeds whose source is the given (just-scored) game.</summary>
    Task<List<AdvancementFeeds>> GetFeedsBySourceAsync(int sourceGid, CancellationToken ct = default);

    /// <summary>
    /// Director-entered seed intent (which pool + rank feeds each slot) for the
    /// given games, from the legacy-in-app BracketSeeds screen. Used to seed the
    /// brackets.* SeedAssignments so cross-pool intent isn't lost.
    /// </summary>
    Task<List<BracketSeeds>> GetBracketSeedsByGidsAsync(
        IReadOnlyCollection<int> gids, CancellationToken ct = default);

    /// <summary>Every leaf seed slot in the job with a resolvable (division, rank).</summary>
    Task<List<SeedSlotToResolve>> GetSeedSlotsForJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// DivIds in the job that still have at least one unscored pool ("T") game —
    /// their standings rank is not yet final, so seeds drawn from them must wait.
    /// </summary>
    Task<HashSet<Guid>> GetIncompletePoolDivIdsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Divisions in the job that have bracket games placed but no BracketInstance —
    /// i.e. need their wiring materialized (backfill). Cheap when none.
    /// </summary>
    Task<List<BracketBackfillTarget>> GetDivisionsWithBracketGamesLackingInstanceAsync(
        Guid jobId, CancellationToken ct = default);

    void AddInstance(BracketInstances instance);

    /// <summary>All bracket strategies (brackets.Strategies), active flag included, name-ordered.</summary>
    Task<List<BracketStrategyDto>> GetStrategiesAsync(CancellationToken ct = default);

    // ── Structural QA reads (read-only) ──

    /// <summary>Every division-scoped bracket instance in the job, with names + template facts.</summary>
    Task<List<BracketInstanceInfo>> GetInstanceInfosForJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Seed assignments materialized for one bracket instance.</summary>
    Task<List<SeedAssignments>> GetSeedAssignmentsByInstanceAsync(
        int bracketInstanceId, CancellationToken ct = default);

    /// <summary>Advancement feeds materialized for one bracket instance.</summary>
    Task<List<AdvancementFeeds>> GetFeedsByInstanceAsync(
        int bracketInstanceId, CancellationToken ct = default);

    /// <summary>Game date for each of the given schedule games (null when unscheduled).</summary>
    Task<Dictionary<int, DateTime?>> GetGDatesByGidsAsync(
        IReadOnlyCollection<int> gids, CancellationToken ct = default);

    /// <summary>Active team count in a division — the valid seed-rank ceiling for a pool.</summary>
    Task<int> GetActiveTeamCountByDivAsync(Guid divId, CancellationToken ct = default);

    /// <summary>A tracked Teams row, for reseed impersonation (rename + clubrep).</summary>
    Task<Teams?> GetTeamTrackedAsync(Guid teamId, CancellationToken ct = default);

    /// <summary>
    /// Raw identity (team name + clubrep registration) for the given source teams,
    /// keyed by TeamId. Used to stamp a flight placeholder during reseeding.
    /// </summary>
    Task<Dictionary<Guid, TeamSeedIdentity>> GetTeamIdentitiesAsync(
        IReadOnlyCollection<Guid> teamIds, CancellationToken ct = default);

    /// <summary>
    /// Idempotent replace: deletes this instance's existing feeds + seeds and
    /// stages the supplied replacements. Caller commits via SaveChangesAsync.
    /// </summary>
    Task ReplaceFeedsAndSeedsAsync(
        int bracketInstanceId,
        IReadOnlyCollection<AdvancementFeeds> feeds,
        IReadOnlyCollection<SeedAssignments> seeds,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
