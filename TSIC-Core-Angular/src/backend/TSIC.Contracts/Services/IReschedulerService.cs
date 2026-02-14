using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for the Rescheduler tool (009-6).
/// Cross-division grid with move/swap, weather adjustment, and bulk email.
/// </summary>
public interface IReschedulerService
{
    // ── Filter Options ──

    /// <summary>
    /// Get CADT filter tree + GameDays + Fields for the rescheduler filter panel.
    /// Reuses the same ScheduleFilterOptionsDto as View Schedule.
    /// </summary>
    Task<ScheduleFilterOptionsDto> GetFilterOptionsAsync(Guid jobId, CancellationToken ct = default);

    // ── Grid ──

    /// <summary>
    /// Build cross-division schedule grid from filter criteria.
    /// Returns all games matching filters, with optional additional timeslot injection.
    /// </summary>
    Task<ScheduleGridResponse> GetReschedulerGridAsync(Guid jobId, ReschedulerGridRequest request, CancellationToken ct = default);

    // ── Move/Swap (identical to ScheduleDivisionService.MoveGameAsync) ──

    /// <summary>
    /// Move a game to a new date/field. If the target slot is occupied, the two games are swapped.
    /// Increments RescheduleCount and updates Modified/LebUserId audit fields.
    /// </summary>
    Task MoveGameAsync(string userId, MoveGameRequest request, CancellationToken ct = default);

    // ── Weather Adjustment ──

    /// <summary>
    /// Preview: count games that would be affected by a weather adjustment.
    /// </summary>
    Task<AffectedGameCountResponse> GetAffectedGameCountAsync(Guid jobId, DateTime preFirstGame, List<Guid> fieldIds, CancellationToken ct = default);

    /// <summary>
    /// Execute weather adjustment via stored procedure [utility].[ScheduleAlterGSIPerGameDate].
    /// Returns human-readable success/error message.
    /// </summary>
    Task<AdjustWeatherResponse> AdjustForWeatherAsync(Guid jobId, AdjustWeatherRequest request, CancellationToken ct = default);

    // ── Email ──

    /// <summary>
    /// Preview: estimated email recipient count for games in a date/field range.
    /// </summary>
    Task<EmailRecipientCountResponse> GetEmailRecipientCountAsync(Guid jobId, DateTime firstGame, DateTime lastGame, List<Guid> fieldIds, CancellationToken ct = default);

    /// <summary>
    /// Send bulk email to all participants of games in a date/field range.
    /// Collects player + parent + club rep + league addon emails, deduplicates, sends via IEmailService.
    /// Logs batch to EmailLogs table.
    /// </summary>
    Task<EmailParticipantsResponse> EmailParticipantsAsync(Guid jobId, string userId, EmailParticipantsRequest request, CancellationToken ct = default);
}
