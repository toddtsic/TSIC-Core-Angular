using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Seed resolution (R1). See <see cref="IBracketSeedResolutionService"/>.
/// Write-forward, mirroring advancement (R2): the occupant of a seeded slot lives
/// on Leagues.schedule (T1Id/T2Id); the (division, rank) that fills it lives in
/// Leagues.BracketSeeds — the director's intent, independent of bracket topology,
/// so a consolation game seeds by this same path; the ranked team comes from
/// pool-play standings.
/// </summary>
public sealed class BracketSeedResolutionService : IBracketSeedResolutionService
{
    private readonly IBracketRepository _bracketRepo;
    private readonly IScheduleRepository _scheduleRepo;
    private readonly IJobRepository _jobRepo;
    private readonly IBracketGenerationService _generation;
    private readonly ILogger<BracketSeedResolutionService> _logger;

    public BracketSeedResolutionService(
        IBracketRepository bracketRepo,
        IScheduleRepository scheduleRepo,
        IJobRepository jobRepo,
        IBracketGenerationService generation,
        ILogger<BracketSeedResolutionService> logger)
    {
        _bracketRepo = bracketRepo;
        _scheduleRepo = scheduleRepo;
        _jobRepo = jobRepo;
        _generation = generation;
        _logger = logger;
    }

    public async Task<int> ResolveJobAsync(
        Guid jobId,
        string userId,
        Func<IReadOnlyCollection<Guid>, CancellationToken, Task<StandingsByDivisionResponse>> standingsProvider,
        CancellationToken ct = default)
    {
        // Seeds read straight from Leagues.BracketSeeds below, so they no longer depend on
        // bracket wiring. This call stays because a pool-game score is also the moment to
        // ensure the ADVANCEMENT feed graph exists for any bracket placed before the schema —
        // otherwise a division scored without a dashboard visit would never advance. Feeds
        // only; idempotent; cheap when everything is already materialized.
        await _generation.EnsureJobWiringAsync(jobId, userId, ct);

        // Cheap gate: nothing to do for a job with no seeded slots.
        var slots = await _bracketRepo.GetSeedSlotsForJobAsync(jobId, ct);
        if (slots.Count == 0) return 0;

        // A pool whose games aren't all scored has no final rank — its seeds wait.
        // Filter BEFORE the standings call: standings is by far the most expensive step
        // here and this method runs on every pool-game score, so a job whose pools are
        // all still in progress must cost two cheap queries, not a full standings sweep.
        var incomplete = await _bracketRepo.GetIncompletePoolDivIdsAsync(jobId, ct);
        var ready = slots.Where(s => !incomplete.Contains(s.SeedDivId)).ToList();
        if (ready.Count == 0) return 0;

        // Standings are already sorted (incl. tiebreak rules) → 1-based position = rank.
        // Scoped to the divisions those ready slots actually draw from: a rank is computed
        // within a division, so this cannot change any result. Job-wide, an event with N
        // pools swept all N every time one completed.
        var seedDivIds = ready.Select(s => s.SeedDivId).Distinct().ToList();
        var standings = await standingsProvider(seedDivIds, ct);
        var teamByDivRank = new Dictionary<(Guid DivId, int Rank), (Guid TeamId, string Name)>();
        foreach (var div in standings.Divisions)
        {
            var rank = 0;
            foreach (var t in div.Teams)
            {
                rank++;
                teamByDivRank[(div.DivId, rank)] = (t.TeamId, t.TeamName);
            }
        }

        // Reseed jobs cross-agegroup: a championship-only flight must behave like any normal
        // agegroup, so its slots keep their own INTERNAL placeholder team. Instead of pointing the
        // schedule at the (foreign-agegroup) source team, we stamp the source college's identity onto
        // the in-slot placeholder (teamName + clubrep_registrationid) and leave Schedule.TxId internal.
        var isReseed = await _jobRepo.GetReseedTournamentFlagAsync(jobId, ct);
        Dictionary<Guid, TeamSeedIdentity> sourceIdentities = [];
        if (isReseed)
        {
            var sourceTeamIds = teamByDivRank.Values.Select(v => v.TeamId).Distinct().ToList();
            sourceIdentities = await _bracketRepo.GetTeamIdentitiesAsync(sourceTeamIds, ct);
        }

        // One tracked query for every target game rather than one per slot.
        var targets = (await _scheduleRepo.GetGamesByIdsAsync(
                ready.Select(s => s.Gid).Distinct().ToList(), ct))
            .ToDictionary(g => g.Gid);

        var now = DateTime.Now;
        var resolved = 0;
        foreach (var slot in ready)
        {
            if (!teamByDivRank.TryGetValue((slot.SeedDivId, slot.SeedRank), out var team))
                continue;                                                            // rank not present (yet)

            if (!targets.TryGetValue(slot.Gid, out var target)) continue;

            // R3 guard: never overwrite a bracket game that has already been played.
            if (target.T1Score.HasValue || target.T2Score.HasValue) continue;

            bool changed = isReseed
                ? await ApplyReseedAsync(target, slot.TargetSlot, team, sourceIdentities, ct)
                : ApplyReference(target, slot.TargetSlot, team);

            if (!changed) continue;
            target.LebUserId = userId;
            target.Modified = now;
            resolved++;
        }

        if (resolved > 0) await _scheduleRepo.SaveChangesAsync(ct);
        _logger.LogInformation(
            "BracketSeedResolve: job {JobId} — {Mode} {N} seed slot(s).",
            jobId, isReseed ? "reseeded" : "filled", resolved);
        return resolved;
    }

    /// <summary>
    /// Normal mode: point the bracket slot directly at the standings-ranked source team.
    /// Returns false when already correct.
    /// </summary>
    private static bool ApplyReference(
        Domain.Entities.Schedule target, byte targetSlot, (Guid TeamId, string Name) team)
    {
        if (targetSlot == 1)
        {
            if (target.T1Id == team.TeamId) return false;
            target.T1Id = team.TeamId;
            target.T1Name = team.Name;
        }
        else
        {
            if (target.T2Id == team.TeamId) return false;
            target.T2Id = team.TeamId;
            target.T2Name = team.Name;
        }
        return true;
    }

    /// <summary>
    /// Reseed mode: stamp the source college's identity onto the flight's INTERNAL placeholder
    /// team (raw teamName + clubrep_registrationid), seat that placeholder in the slot, and set
    /// the schedule's display name — Schedule.TxId stays pointed at the internal id.
    ///
    /// The placeholder is DERIVED from the flight's own division at the slot's seed line
    /// (Teams.DivRank == the slot's TxNo — the same lookup that seats a round-robin slot), not
    /// read back off Schedule.TxId. A bracket slot is minted empty and stays empty until this
    /// runs, so reading the occupant would make the reseed depend on a value nothing writes and
    /// would make a schedule reset unrecoverable.
    /// </summary>
    private async Task<bool> ApplyReseedAsync(
        Domain.Entities.Schedule target, byte targetSlot,
        (Guid TeamId, string Name) source,
        IReadOnlyDictionary<Guid, TeamSeedIdentity> sourceIdentities,
        CancellationToken ct)
    {
        if (target.DivId is null) return false;
        if (!sourceIdentities.TryGetValue(source.TeamId, out var src)) return false;

        int? seedLine = targetSlot == 1 ? target.T1No : target.T2No;
        if (seedLine is null) return false;

        var placeholder = await _bracketRepo.GetTeamTrackedByDivRankAsync(
            target.DivId.Value, seedLine.Value, ct);
        if (placeholder is null) return false;                        // no placeholder for this seed line

        var changed = false;
        if (placeholder.TeamName != src.TeamName)
        {
            placeholder.TeamName = src.TeamName;                      // raw college name; teamFullName untouched
            changed = true;
        }
        if (placeholder.ClubrepRegistrationid != src.ClubrepRegistrationid)
        {
            placeholder.ClubrepRegistrationid = src.ClubrepRegistrationid;  // drives club/college display
            changed = true;
        }

        // Seat the placeholder and label the slot with the source's club-prefixed standings
        // name. The id stays internal to the flight; only the identity travels.
        if (targetSlot == 1)
        {
            if (target.T1Id != placeholder.TeamId) { target.T1Id = placeholder.TeamId; changed = true; }
            if (target.T1Name != source.Name) { target.T1Name = source.Name; changed = true; }
        }
        else
        {
            if (target.T2Id != placeholder.TeamId) { target.T2Id = placeholder.TeamId; changed = true; }
            if (target.T2Name != source.Name) { target.T2Name = source.Name; changed = true; }
        }
        return changed;
    }
}
