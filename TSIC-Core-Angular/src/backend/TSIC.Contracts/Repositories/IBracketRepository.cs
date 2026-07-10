using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Data access for the brackets schema. Reads placed bracket games from
/// Leagues.schedule, reads/writes the advancement feed graph (instances + feeds),
/// and reads seed intent from Leagues.BracketSeeds. Templates/routes are read-only
/// reference topology (seeded by SQL).
///
/// Seed intent is NOT stored in the brackets schema. It lives in Leagues.BracketSeeds,
/// keyed on the schedule game and side, independent of any template — so a seeded game
/// need not belong to a bracket. Feeds (outcome edges) are the only thing genuinely
/// template-derived, and the only thing this schema materializes.
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
    /// Every seeded slot in the job with a resolvable (division, rank), read from the
    /// director's intent in Leagues.BracketSeeds. Includes consolation slots — any
    /// non-pool game whose side carries a seed — because seeding does not depend on
    /// bracket membership.
    /// </summary>
    Task<List<SeedSlotToResolve>> GetSeedSlotsForJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// The same seed-slot projection as <see cref="GetSeedSlotsForJobAsync"/>, scoped to a
    /// specific set of games. For QA seed-coverage checks over one bracket instance's placed games.
    /// </summary>
    Task<List<SeedSlotToResolve>> GetSeedSlotsByGidsAsync(
        IReadOnlyCollection<int> gids, CancellationToken ct = default);

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

    /// <summary>Advancement feeds materialized for one bracket instance.</summary>
    Task<List<AdvancementFeeds>> GetFeedsByInstanceAsync(
        int bracketInstanceId, CancellationToken ct = default);

    /// <summary>Game date for each of the given schedule games (null when unscheduled).</summary>
    Task<Dictionary<int, DateTime?>> GetGDatesByGidsAsync(
        IReadOnlyCollection<int> gids, CancellationToken ct = default);

    /// <summary>Active team count in a division — the valid seed-rank ceiling for a pool.</summary>
    Task<int> GetActiveTeamCountByDivAsync(Guid divId, CancellationToken ct = default);

    /// <summary>
    /// The flight's INTERNAL placeholder team seated at a bracket seed line: the active team in
    /// <paramref name="divId"/> whose DivRank matches the slot's TxNo. Tracked, for reseed
    /// impersonation (rename + clubrep). Derived rather than read off Schedule.TxId, which a
    /// schedule reset legitimately clears.
    /// </summary>
    Task<Teams?> GetTeamTrackedByDivRankAsync(
        Guid divId, int divRank, CancellationToken ct = default);

    /// <summary>
    /// Raw identity (team name + clubrep registration) for the given source teams,
    /// keyed by TeamId. Used to stamp a flight placeholder during reseeding.
    /// </summary>
    Task<Dictionary<Guid, TeamSeedIdentity>> GetTeamIdentitiesAsync(
        IReadOnlyCollection<Guid> teamIds, CancellationToken ct = default);

    /// <summary>
    /// Idempotent replace of one instance's advancement feed graph: deletes its existing
    /// feeds and stages the supplied replacements. Also drains any legacy SeedAssignments
    /// rows for the instance — seed intent now lives in Leagues.BracketSeeds and this
    /// schema no longer stores it. Caller commits via SaveChangesAsync.
    /// </summary>
    Task ReplaceFeedsAsync(
        int bracketInstanceId,
        IReadOnlyCollection<AdvancementFeeds> feeds,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
