using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for the View Schedule page (009-5).
/// Consumer-facing schedule viewer with five tabs: Games, Standings, Records, Brackets, Contacts.
/// Supports both authenticated and public access modes.
/// </summary>
public interface IViewScheduleService
{
    /// <summary>
    /// Get filter options (CADT tree + game days + fields) and capability flags.
    /// Called once on component init.
    /// </summary>
    Task<ScheduleFilterOptionsDto> GetFilterOptionsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get capability flags for the current user/job (canScore, hideContacts, sportName).
    /// </summary>
    Task<ScheduleCapabilitiesDto> GetCapabilitiesAsync(Guid jobId, bool isAuthenticated, bool isAdmin, CancellationToken ct = default);

    /// <summary>
    /// Games tab — filtered schedule games ordered by date.
    /// </summary>
    Task<List<ViewGameDto>> GetGamesAsync(Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default);

    /// <summary>
    /// Games tab, server-side paged. Applies request.Skip/Take and also returns the total match
    /// count (before paging) for the X-Total-Count header. Skip/Take both absent ⇒ identical body
    /// to <see cref="GetGamesAsync"/> with TotalCount = the full row count.
    /// </summary>
    Task<(List<ViewGameDto> Games, int TotalCount)> GetGamesPagedAsync(Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default);

    /// <summary>
    /// Standings tab — pool play standings grouped by division.
    /// Only includes games where T1Type == "T" AND T2Type == "T".
    /// </summary>
    Task<StandingsByDivisionResponse> GetStandingsAsync(Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default);

    /// <summary>
    /// Standings tab, server-side paged over the ORDERED divisions list (a division is the
    /// standings unit). Applies request.Skip/Take to the divisions and returns the total division
    /// count for the filter. Records are still computed from the full game set, so paging never
    /// distorts W-L-T. Skip/Take both absent ⇒ identical body to <see cref="GetStandingsAsync"/>.
    /// </summary>
    Task<(StandingsByDivisionResponse Response, int TotalCount)> GetStandingsPagedAsync(Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default);

    /// <summary>
    /// Records tab — full season records (all game types) grouped by division.
    /// </summary>
    Task<StandingsByDivisionResponse> GetTeamRecordsAsync(Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default);

    /// <summary>
    /// Team results drill-down — all games for a specific team, plus team identity for the modal title.
    /// </summary>
    Task<TeamResultsResponse> GetTeamResultsAsync(Guid teamId, CancellationToken ct = default);

    /// <summary>
    /// Brackets tab — bracket matches grouped by division (or agegroup).
    /// </summary>
    Task<List<DivisionBracketResponse>> GetBracketsAsync(Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default);

    /// <summary>
    /// Contacts tab — staff contacts for teams in the filtered schedule.
    /// </summary>
    Task<List<ContactDto>> GetContactsAsync(Guid jobId, ScheduleFilterRequest request, CancellationToken ct = default);

    // ── Mobile deep-link lookups ──
    // The Events app navigates from a game row or team where it holds only a gid/teamId
    // and cannot compose a division filter. Each resolves the owning job + division,
    // then delegates to the matching tab method. Null = game/team not found.

    /// <summary>Bracket matches for the division (or agegroup) a game belongs to.</summary>
    Task<List<DivisionBracketResponse>?> GetBracketsByGameAsync(int gid, CancellationToken ct = default);

    /// <summary>Bracket matches for the division (or agegroup) a team belongs to.</summary>
    Task<List<DivisionBracketResponse>?> GetBracketsByTeamAsync(Guid teamId, CancellationToken ct = default);

    /// <summary>Pool standings for the division a game belongs to.</summary>
    Task<StandingsByDivisionResponse?> GetStandingsByGameAsync(int gid, CancellationToken ct = default);

    /// <summary>Pool standings for the division a team belongs to.</summary>
    Task<StandingsByDivisionResponse?> GetStandingsByTeamAsync(Guid teamId, CancellationToken ct = default);

    /// <summary>
    /// Field directions / details for a specific field.
    /// </summary>
    Task<FieldDisplayDto?> GetFieldInfoAsync(Guid fieldId, CancellationToken ct = default);

    /// <summary>
    /// Quick inline score edit — updates T1Score, T2Score, and optionally GStatusCode.
    /// </summary>
    Task QuickEditScoreAsync(Guid jobId, string userId, EditScoreRequest request, CancellationToken ct = default);

    /// <summary>
    /// Full game edit — supports overriding teams, annotations, scores, and status.
    /// </summary>
    Task EditGameAsync(Guid jobId, string userId, EditGameRequest request, CancellationToken ct = default);

    /// <summary>
    /// One-time deploy-time backfill: recompute every team's stored pool record from scratch via
    /// the canonical method and overwrite the Teams columns (teams with no scored games are zeroed).
    /// Must run once at cutover before the stored-column read paths (games-grid records, pool
    /// standings, seeding) are trusted; thereafter score entry keeps the columns fresh. Returns the
    /// number of teams written.
    /// </summary>
    Task<int> RebuildTeamRecordsAsync(Guid jobId, CancellationToken ct = default);
}
