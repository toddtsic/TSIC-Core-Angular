using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for the Manage Pairings scheduling tool.
/// Handles round-robin generation, single-elimination bracket cascade,
/// pairing CRUD, and the who-plays-who matrix.
/// </summary>
public interface IPairingsService
{
    /// <summary>
    /// Get the agegroup → division navigator tree for the current league-season.
    /// </summary>
    Task<List<AgegroupWithDivisionsDto>> GetAgegroupsWithDivisionsAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get all pairings for a specific division.
    /// </summary>
    Task<DivisionPairingsResponse> GetDivisionPairingsAsync(
        Guid jobId, Guid divId, CancellationToken ct = default);

    /// <summary>
    /// Compute the N×N who-plays-who matrix for a given team count.
    /// </summary>
    Task<WhoPlaysWhoResponse> GetWhoPlaysWhoAsync(
        Guid jobId, int teamCount, CancellationToken ct = default);

    /// <summary>
    /// Add a round-robin block from Masterpairingtable templates.
    /// </summary>
    Task<List<PairingDto>> AddPairingBlockAsync(
        Guid jobId, string userId, AddPairingBlockRequest request, CancellationToken ct = default);

    /// <summary>
    /// Add single-elimination bracket, cascading from startKey through Finals.
    /// </summary>
    Task<List<PairingDto>> AddSingleEliminationAsync(
        Guid jobId, string userId, AddSingleEliminationRequest request, CancellationToken ct = default);

    /// <summary>
    /// Add a single blank pairing row for manual entry.
    /// </summary>
    Task<PairingDto> AddSinglePairingAsync(
        Guid jobId, string userId, AddSinglePairingRequest request, CancellationToken ct = default);

    /// <summary>
    /// Inline edit of an existing pairing.
    /// </summary>
    Task EditPairingAsync(
        string userId, EditPairingRequest request, CancellationToken ct = default);

    /// <summary>
    /// Delete a single pairing by primary key.
    /// </summary>
    Task DeletePairingAsync(int ai, CancellationToken ct = default);

    /// <summary>
    /// Remove ALL pairings for a given team count within the current league-season.
    /// </summary>
    Task RemoveAllPairingsAsync(
        Guid jobId, RemoveAllPairingsRequest request, CancellationToken ct = default);

    // ── Division Teams ──

    /// <summary>
    /// Get all active teams in a division, ordered by DivRank, with club names.
    /// </summary>
    Task<List<DivisionTeamDto>> GetDivisionTeamsAsync(
        Guid jobId, Guid divId, CancellationToken ct = default);

    /// <summary>
    /// Edit a team's rank and/or name. Rank changes perform an atomic swap.
    /// After changes, renumbers ranks and synchronizes schedule records.
    /// Returns the refreshed team list.
    /// </summary>
    Task<List<DivisionTeamDto>> EditDivisionTeamAsync(
        Guid jobId, string userId, EditDivisionTeamRequest request, CancellationToken ct = default);
}
