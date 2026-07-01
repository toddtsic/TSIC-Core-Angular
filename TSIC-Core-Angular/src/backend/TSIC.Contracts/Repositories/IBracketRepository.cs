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

    Task<Templates?> GetSeTemplateAsync(int bracketSize, CancellationToken ct = default);

    Task<List<TemplateGames>> GetTemplateGamesAsync(int templateId, CancellationToken ct = default);

    Task<List<AdvancementRoutes>> GetTemplateRoutesAsync(int templateId, CancellationToken ct = default);

    Task<BracketInstances?> GetInstanceAsync(
        Guid jobId, Guid agegroupId, Guid divId, CancellationToken ct = default);

    /// <summary>Materialized feeds whose source is the given (just-scored) game.</summary>
    Task<List<AdvancementFeeds>> GetFeedsBySourceAsync(int sourceGid, CancellationToken ct = default);

    void AddInstance(BracketInstances instance);

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
