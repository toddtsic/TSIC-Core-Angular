namespace TSIC.Contracts.Dtos.Scheduling;

// ══════════════════════════════════════════════════════════════════════
// Grid Request (extends ScheduleFilterRequest pattern with additional timeslot)
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// POST body for the rescheduler grid — same filter structure as View Schedule
/// plus optional additional timeslot injection.
/// All multi-select filters use List&lt;T&gt;? (null = no filter, OR-union logic).
/// </summary>
public record ReschedulerGridRequest
{
    public List<string>? ClubNames { get; init; }
    public List<Guid>? AgegroupIds { get; init; }
    public List<Guid>? DivisionIds { get; init; }
    public List<Guid>? TeamIds { get; init; }
    public List<DateTime>? GameDays { get; init; }
    public List<Guid>? FieldIds { get; init; }

    /// <summary>
    /// Optional datetime to inject as an additional row in the grid.
    /// Allows admin to create a new timeslot that doesn't exist in the timeslot configuration.
    /// </summary>
    public DateTime? AdditionalTimeslot { get; init; }
}

// ══════════════════════════════════════════════════════════════════════
// Weather Adjustment
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// Adjusts game times in batch by recomputing each game's slot from a "before"
/// GSI grid onto an "after" GSI grid. "Before" = current schedule, "After" = desired.
/// </summary>
public record AdjustWeatherRequest
{
    public required DateTime PreFirstGame { get; init; }
    public required int PreGSI { get; init; }
    public required DateTime PostFirstGame { get; init; }
    public required int PostGSI { get; init; }
    public required List<Guid> FieldIds { get; init; }
}

public record AdjustWeatherResponse
{
    public required bool Success { get; init; }
    public required int ResultCode { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// Preview: how many games would be affected by a weather adjustment.
/// </summary>
public record AffectedGameCountResponse
{
    public required int Count { get; init; }
}

// ══════════════════════════════════════════════════════════════════════
// Email Participants
// ══════════════════════════════════════════════════════════════════════

public record EmailParticipantsRequest
{
    public required DateTime FirstGame { get; init; }
    public required DateTime LastGame { get; init; }
    public required string EmailSubject { get; init; }
    public required string EmailBody { get; init; }
    public required List<Guid> FieldIds { get; init; }
}

/// <summary>
/// Preview: estimated recipient count before actually sending.
/// </summary>
public record EmailRecipientCountResponse
{
    public required int EstimatedCount { get; init; }
}

/// <summary>
/// One resolved reschedule-email recipient. Carries the registration the address belongs to so the
/// shared batch engine can append the per-reg unsubscribe footer and honor <c>BemailOptOut</c>:
/// roster players + club reps carry their <see cref="RegistrationId"/>.
///
/// League reschedule-addon contacts (<c>Leagues.RescheduleEmailsToAddon</c>) carry
/// <see cref="RegistrationId"/> = null BY DESIGN — they get no footer and are never suppressed.
/// This is a deliberate product decision (Todd, 2026-06-25), not a fallback: addons are a
/// director-configured operational distribution list (commissioner / field-coordinator inboxes)
/// that must always receive reschedule notices; they have no registration to unsubscribe, and an
/// inert <c>?regId=</c> link would be a worse (CAN-SPAM) outcome than no link. "Unsubscribing" an
/// addon means the director removes it from the league config.
/// </summary>
public record ScheduleEmailRecipient
{
    public Guid? RegistrationId { get; init; }
    public required string Email { get; init; }
    public bool OptedOut { get; init; }
}

// ══════════════════════════════════════════════════════════════════════
// Shared DTOs (NOT defined here — reuse from existing files)
// ══════════════════════════════════════════════════════════════════════
// MoveGameRequest            → ScheduleDivisionDtos.cs
// ScheduleGridResponse       → ScheduleDivisionDtos.cs (Columns + Rows)
// ScheduleGridRow            → ScheduleDivisionDtos.cs (GDate + Cells)
// ScheduleGameDto            → ScheduleDivisionDtos.cs (game cell with Color, T1Id/T2Id)
// ScheduleFieldColumn        → ScheduleDivisionDtos.cs (FieldId + FName)
// ScheduleFilterOptionsDto   → ViewScheduleDtos.cs (CADT tree + GameDays + Fields)
