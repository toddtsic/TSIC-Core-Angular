namespace TSIC.Contracts.Dtos.Scheduling;

// ══════════════════════════════════════════════════════════
// Scheduling Cascade Snapshot — resolved 3-level cascade
// Event → Agegroup → Division for GamePlacement, BetweenRoundRows, Wave
// ══════════════════════════════════════════════════════════

/// <summary>
/// Complete resolved cascade for a job. Every division has effective values
/// for GamePlacement, BetweenRoundRows, and per-date Wave.
/// </summary>
public record ScheduleCascadeSnapshot
{
    public required EventScheduleDefaultsDto EventDefaults { get; init; }
    public required List<AgegroupCascadeDto> Agegroups { get; init; }
}

/// <summary>
/// Event-level defaults (floor of the cascade). Non-nullable — always has values.
/// GamePlacement: "H" (Horizontal) or "V" (Vertical).
/// BetweenRoundRows: 0 (back-to-back), 1 (one game break), 2 (two game break).
/// </summary>
public record EventScheduleDefaultsDto
{
    public required string GamePlacement { get; init; }
    public required byte BetweenRoundRows { get; init; }
}

/// <summary>
/// Agegroup-level cascade node. Override values are null when inheriting from event.
/// </summary>
public record AgegroupCascadeDto
{
    public required Guid AgegroupId { get; init; }
    public required string AgegroupName { get; init; }

    /// <summary>Null = inheriting GamePlacement from event.</summary>
    public string? GamePlacementOverride { get; init; }

    /// <summary>Null = inheriting BetweenRoundRows from event.</summary>
    public byte? BetweenRoundRowsOverride { get; init; }

    /// <summary>Resolved: override ?? event default.</summary>
    public required string EffectiveGamePlacement { get; init; }

    /// <summary>Resolved: override ?? event default.</summary>
    public required byte EffectiveBetweenRoundRows { get; init; }

    /// <summary>Per-date agegroup wave (date → wave). Empty = no agegroup-level waves set.</summary>
    public required Dictionary<DateTime, byte> WavesByDate { get; init; }

    public required List<DivisionCascadeDto> Divisions { get; init; }
}

/// <summary>
/// Division-level cascade node (finest grain). Override values are null when inheriting from agegroup.
/// </summary>
public record DivisionCascadeDto
{
    public required Guid DivisionId { get; init; }
    public required string DivisionName { get; init; }

    /// <summary>Null = inheriting GamePlacement from agegroup.</summary>
    public string? GamePlacementOverride { get; init; }

    /// <summary>Null = inheriting BetweenRoundRows from agegroup.</summary>
    public byte? BetweenRoundRowsOverride { get; init; }

    /// <summary>Resolved: div.Override ?? ag.Override ?? event default.</summary>
    public required string EffectiveGamePlacement { get; init; }

    /// <summary>Resolved: div.Override ?? ag.Override ?? event default.</summary>
    public required byte EffectiveBetweenRoundRows { get; init; }

    /// <summary>
    /// Per-date effective wave (date → wave).
    /// Resolved: divWave(date) ?? agWave(date) ?? 1.
    /// Only includes dates where a wave is explicitly set at any level.
    /// </summary>
    public required Dictionary<DateTime, byte> EffectiveWavesByDate { get; init; }
}

// ══════════════════════════════════════════════════════════
// Save Request DTOs (for API endpoints)
// ══════════════════════════════════════════════════════════

/// <summary>
/// Request to save event-level defaults.
/// </summary>
public record SaveEventDefaultsRequest
{
    /// <summary>"H" or "V"</summary>
    public required string GamePlacement { get; init; }

    /// <summary>0, 1, or 2</summary>
    public required byte BetweenRoundRows { get; init; }
}

/// <summary>
/// Request to save agegroup or division level overrides + wave assignments.
/// Null properties = "inherit from parent" (clears existing override).
/// </summary>
public record SaveCascadeLevelRequest
{
    /// <summary>"H", "V", or null (inherit from parent).</summary>
    public string? GamePlacement { get; init; }

    /// <summary>0, 1, 2, or null (inherit from parent).</summary>
    public byte? BetweenRoundRows { get; init; }

    /// <summary>
    /// Per-date wave assignments (ISO date string → wave 1-3).
    /// Empty/null = clear all wave assignments for this level.
    /// </summary>
    public Dictionary<string, byte>? WavesByDate { get; init; }
}
