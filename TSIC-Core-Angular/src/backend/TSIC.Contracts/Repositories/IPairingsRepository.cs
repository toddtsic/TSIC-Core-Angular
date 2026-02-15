using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for pairings data access — round-robin templates, bracket seeds,
/// and mutable PairingsLeagueSeason records.
/// </summary>
public interface IPairingsRepository
{
    // ── Read: PairingsLeagueSeason ──

    /// <summary>
    /// Get all pairings for a league-season and team count.
    /// </summary>
    Task<List<PairingsLeagueSeason>> GetPairingsAsync(
        Guid leagueId, string season, int teamCount, CancellationToken ct = default);

    /// <summary>
    /// Get the current max GameNumber and max Rnd for a league-season and team count.
    /// Returns (0, 0) if no pairings exist yet.
    /// </summary>
    Task<(int maxGameNumber, int maxRound)> GetMaxGameAndRoundAsync(
        Guid leagueId, string season, int teamCount, CancellationToken ct = default);

    /// <summary>
    /// Get a single pairing by primary key (tracked for updates).
    /// </summary>
    Task<PairingsLeagueSeason?> GetByIdAsync(int ai, CancellationToken ct = default);

    // ── Read: Seed data ──

    /// <summary>
    /// Get round-robin master pairing templates for a given team count and max round count.
    /// </summary>
    Task<List<Masterpairingtable>> GetMasterPairingsAsync(
        int teamCount, int maxRounds, CancellationToken ct = default);

    /// <summary>
    /// Get bracket seed data for a given round type (Z, Y, X, Q, S, F).
    /// Ordered by T1 ascending.
    /// </summary>
    Task<List<BracketDataSingleElimination>> GetBracketDataAsync(
        string roundType, CancellationToken ct = default);

    // ── Read: Availability ──

    /// <summary>
    /// Get the set of (Rnd, T1, T2) keys that already have a scheduled game
    /// for the specific division. Used to determine BAvailable status.
    /// </summary>
    Task<HashSet<(int Rnd, int T1, int T2)>> GetScheduledPairingKeysAsync(
        Guid leagueId, string season, Guid divId, CancellationToken ct = default);

    // ── Read: Agegroup/Division tree ──

    /// <summary>
    /// Get agegroups with divisions and team counts for a league-season.
    /// </summary>
    Task<List<Agegroups>> GetAgegroupsWithDivisionsAsync(
        Guid leagueId, string season, CancellationToken ct = default);

    /// <summary>
    /// Get team count for a division (active teams only).
    /// </summary>
    Task<int> GetDivisionTeamCountAsync(Guid divId, Guid jobId, CancellationToken ct = default);

    // ── Write ──

    /// <summary>
    /// Bulk insert pairings.
    /// </summary>
    Task AddRangeAsync(List<PairingsLeagueSeason> pairings, CancellationToken ct = default);

    /// <summary>
    /// Delete a single pairing.
    /// </summary>
    void Remove(PairingsLeagueSeason pairing);

    /// <summary>
    /// Delete ALL pairings for a league-season and team count.
    /// </summary>
    Task DeleteAllAsync(Guid leagueId, string season, int teamCount, CancellationToken ct = default);

    /// <summary>
    /// Persist changes.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
