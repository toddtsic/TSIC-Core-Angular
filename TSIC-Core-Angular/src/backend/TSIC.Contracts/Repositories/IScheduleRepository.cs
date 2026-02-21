using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing Schedule entity data access.
/// </summary>
public interface IScheduleRepository
{
    /// <summary>
    /// Single point of truth for schedule team-name sync.
    /// Re-composes T1Name/T2Name from source entities (Teams.TeamName, Registrations.ClubName,
    /// Jobs.BShowTeamNameOnlyInSchedules) for every round-robin game where this team appears.
    /// Idempotent — safe to call from any admin operation that changes team name, club, or the flag.
    /// Only touches games where T1Type/T2Type == "T" (round-robin); championship/bracket games
    /// are updated by score-entry logic, not here.
    /// </summary>
    Task SynchronizeScheduleNamesForTeamAsync(Guid teamId, Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Update denormalized AgegroupId/AgegroupName/DivId/DivName on Schedule records
    /// for a team that has been moved to a different division/agegroup.
    /// Only touches round-robin games (T1Type/T2Type == "T").
    /// </summary>
    Task<int> SynchronizeScheduleDivisionForTeamAsync(
        Guid teamId, Guid jobId, Guid newAgegroupId, string newAgegroupName,
        Guid newDivId, string newDivName, CancellationToken ct = default);

    /// <summary>
    /// Update denormalized AgegroupName on all Schedule records where AgegroupId matches.
    /// Called when an agegroup is renamed in the LADT editor.
    /// </summary>
    Task SynchronizeScheduleAgegroupNameAsync(Guid agegroupId, Guid jobId, string newName, CancellationToken ct = default);

    /// <summary>
    /// Update denormalized DivName (and Div2Name) on all Schedule records where
    /// DivId (or Div2Id) matches. Called when a division is renamed in the LADT editor.
    /// </summary>
    Task SynchronizeScheduleDivisionNameAsync(Guid divId, Guid jobId, string newName, CancellationToken ct = default);

    /// <summary>
    /// Re-resolve T1Id/T1Name and T2Id/T2Name for every round-robin schedule record
    /// in a division based on current DivRank assignments. Called after a DivRank swap
    /// or team rename to keep denormalized fields in sync.
    /// Builds a rank → (teamId, displayName) map from active teams, then updates
    /// every game where DivId matches and T1Type/T2Type == "T".
    /// </summary>
    Task SynchronizeScheduleTeamAssignmentsForDivisionAsync(Guid divId, Guid jobId, CancellationToken ct = default);

    // ── Schedule Division (009-4) ──

    /// <summary>
    /// Get all scheduled games visible on the grid for a given set of fields and dates.
    /// Returns games across all divisions that fall on the specified dates and fields.
    /// </summary>
    Task<List<Schedule>> GetGamesForGridAsync(
        Guid jobId, List<Guid> fieldIds, List<DateTime> gameDates, CancellationToken ct = default);

    /// <summary>
    /// Get all occupied (FieldId, GDate) pairs for a set of fields.
    /// Used by auto-schedule to avoid double-booking slots across divisions.
    /// </summary>
    Task<HashSet<(Guid fieldId, DateTime gDate)>> GetOccupiedSlotsAsync(
        Guid jobId, List<Guid> fieldIds, CancellationToken ct = default);

    /// <summary>
    /// Get a single schedule record by Gid (tracked for mutation).
    /// </summary>
    Task<Schedule?> GetGameByIdAsync(int gid, CancellationToken ct = default);

    /// <summary>
    /// Find the schedule record at a specific date/field intersection (tracked for mutation).
    /// </summary>
    Task<Schedule?> GetGameAtSlotAsync(DateTime gDate, Guid fieldId, CancellationToken ct = default);

    /// <summary>
    /// Add a new schedule record.
    /// </summary>
    void AddGame(Schedule game);

    /// <summary>
    /// Delete a single game and its cascade dependents (DeviceGids, BracketSeeds).
    /// </summary>
    Task DeleteGameAsync(int gid, CancellationToken ct = default);

    /// <summary>
    /// Delete all games for a division (DivId or Div2Id match) with cascade cleanup.
    /// </summary>
    Task DeleteDivisionGamesAsync(Guid divId, Guid leagueId, string season, string year, CancellationToken ct = default);

    /// <summary>Persist pending changes.</summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    // ── View Schedule (009-5) ──

    /// <summary>
    /// Get all games for a job, filtered by the user's preferences.
    /// Includes Field navigation for lat/lng and Agegroup for color.
    /// Ordered by GDate ascending.
    /// </summary>
    Task<List<Schedule>> GetFilteredGamesAsync(Guid jobId, Dtos.Scheduling.ScheduleFilterRequest request, CancellationToken ct = default);

    /// <summary>
    /// Get the CADT filter tree (Club → Agegroup → Division → Team) for a job.
    /// Only includes teams that appear in at least one scheduled game.
    /// </summary>
    Task<Dtos.Scheduling.ScheduleFilterOptionsDto> GetScheduleFilterOptionsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get all games for a specific team (both as T1 and T2), for the team results drill-down.
    /// Includes Field navigation for location name.
    /// </summary>
    Task<List<Schedule>> GetTeamGamesAsync(Guid teamId, CancellationToken ct = default);

    /// <summary>
    /// Get bracket games for a job, optionally filtered by agegroup/division.
    /// Returns games where T1Type or T2Type is a bracket type (Q, S, F, X, Y, Z).
    /// </summary>
    Task<List<Schedule>> GetBracketGamesAsync(Guid jobId, Dtos.Scheduling.ScheduleFilterRequest request, CancellationToken ct = default);

    /// <summary>
    /// Get staff contacts for teams in the filtered schedule.
    /// Returns registrations with Staff role assigned to teams that appear in games.
    /// </summary>
    Task<List<Dtos.Scheduling.ContactDto>> GetContactsAsync(Guid jobId, Dtos.Scheduling.ScheduleFilterRequest request, CancellationToken ct = default);

    /// <summary>
    /// Get field display details (address, directions, coordinates).
    /// </summary>
    Task<Dtos.Scheduling.FieldDisplayDto?> GetFieldDisplayAsync(Guid fieldId, CancellationToken ct = default);

    /// <summary>
    /// Get the sport name for a job (via Jobs → Sports navigation).
    /// Used to determine standings sort order (soccer vs lacrosse).
    /// </summary>
    Task<string> GetSportNameAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get schedule-related flags for a job: BScheduleAllowPublicAccess, BHideContacts, SportName.
    /// </summary>
    Task<(bool allowPublicAccess, bool hideContacts, string sportName)> GetScheduleFlagsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Return the subset of agegroup IDs where BChampionsByDivision = true.
    /// Used by brackets tab to determine per-division vs per-agegroup grouping.
    /// </summary>
    Task<HashSet<Guid>> GetChampionsByDivisionAgegroupIdsAsync(
        IEnumerable<Guid> agegroupIds, CancellationToken ct = default);

    // ── Dashboard ──

    /// <summary>
    /// Returns (totalGames, distinctDivisionIds) for the scheduling dashboard status cards.
    /// </summary>
    Task<(int GameCount, int DivisionsScheduled)> GetSchedulingDashboardStatsAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get the count of round-robin (T1Type="T" AND T2Type="T") scheduled games
    /// grouped by DivId. Used by dashboard to determine fully-scheduled divisions.
    /// </summary>
    Task<Dictionary<Guid, int>> GetRoundRobinGameCountsByDivisionAsync(
        Guid jobId, CancellationToken ct = default);

    // ── Rescheduler (009-6) ──

    /// <summary>
    /// Cross-division grid — returns ALL games matching filters (not scoped to one division).
    /// Reuses ScheduleGridResponse (same Columns/Rows/Cells shape as Schedule Division).
    /// If additionalTimeslot is provided, injects an extra empty row at that datetime.
    /// </summary>
    Task<Dtos.Scheduling.ScheduleGridResponse> GetReschedulerGridAsync(
        Guid jobId, Dtos.Scheduling.ReschedulerGridRequest request, CancellationToken ct = default);

    /// <summary>
    /// Count games in a date/field range — used for weather adjustment preview.
    /// </summary>
    Task<int> GetAffectedGameCountAsync(
        Guid jobId, DateTime preFirstGame, List<Guid> fieldIds, CancellationToken ct = default);

    /// <summary>
    /// Collect and deduplicate email addresses for games in a date/field range.
    /// Sources: player, mom, dad, club rep, league reschedule addon.
    /// Filters: removes nulls, empty, "not@given.com", invalid emails.
    /// </summary>
    Task<List<string>> GetEmailRecipientsAsync(
        Guid jobId, DateTime firstGame, DateTime lastGame, List<Guid> fieldIds, CancellationToken ct = default);

    /// <summary>
    /// Execute stored procedure [utility].[ScheduleAlterGSIPerGameDate].
    /// Returns the int result code (1=success, 2-8=error).
    /// </summary>
    Task<int> ExecuteWeatherAdjustmentAsync(
        Guid jobId, Dtos.Scheduling.AdjustWeatherRequest request, CancellationToken ct = default);
}
