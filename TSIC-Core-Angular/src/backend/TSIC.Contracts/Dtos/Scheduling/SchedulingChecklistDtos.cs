namespace TSIC.Contracts.Dtos.Scheduling;

/// <summary>
/// Ordered readiness checklist for taking an event from zero games to a built schedule.
/// One readiness source: the checklist page and the hub's build gate both derive from this
/// same data so they can never disagree. Read-only — computing status never writes.
/// </summary>
public record SchedulingChecklistDto
{
    /// <summary>Step 1 — every active team sits in a pool other than "Unassigned".</summary>
    public required ChecklistPoolsStepDto Pools { get; init; }

    /// <summary>
    /// Step 2 — every schedulable agegroup has at least one play date. Agegroup-grained by
    /// design: play dates are set for the whole agegroup, not per pool.
    /// </summary>
    public required ChecklistAgegroupStepDto Dates { get; init; }

    /// <summary>
    /// Step 3 — every schedulable division resolves at least one field. Division-grained:
    /// field timeslots are authored per pool, so "2030 Red" can be ready while
    /// "2030 White" is not.
    /// </summary>
    public required ChecklistDivisionStepDto Fields { get; init; }

    /// <summary>Step 4 — every schedulable division resolves a game guarantee &gt; 0.</summary>
    public required ChecklistRulesStepDto Rules { get; init; }

    /// <summary>Step 5 — informational: missing pairings are auto-generated at build.</summary>
    public required ChecklistPairingsStepDto Pairings { get; init; }

    /// <summary>Scheduled games currently on the books for this job.</summary>
    public required int GameCount { get; init; }

    /// <summary>True when steps 1–4 are all complete — the build may proceed.</summary>
    public required bool BuildUnlocked { get; init; }

    /// <summary>Whole-schedule aggregates for the post-build stats band.</summary>
    public required ChecklistScheduleStatsDto ScheduleStats { get; init; }
}

/// <summary>
/// At-a-glance aggregates over the built schedule. All zeros / nulls until games exist —
/// the client gates the stats band on <see cref="SchedulingChecklistDto.GameCount"/>.
/// </summary>
public record ChecklistScheduleStatsDto
{
    public required int GameCount { get; init; }
    /// <summary>Distinct divisions holding at least one scheduled game.</summary>
    public required int DivisionsScheduled { get; init; }
    /// <summary>Distinct fields holding at least one scheduled game.</summary>
    public required int FieldsInUse { get; init; }
    /// <summary>Distinct calendar days with games.</summary>
    public required int PlayDateCount { get; init; }
    /// <summary>Earliest game day (date only), null when no games.</summary>
    public required DateTime? FirstGameDate { get; init; }
    /// <summary>Latest game day (date only), null when no games.</summary>
    public required DateTime? LastGameDate { get; init; }
}

/// <summary>Pool-assignment readiness. Offenders are agegroups holding unpooled teams.</summary>
public record ChecklistPoolsStepDto
{
    public required bool Complete { get; init; }
    public required int TotalUnpooledTeams { get; init; }
    public required List<ChecklistAgegroupPoolsDto> Offenders { get; init; }
}

/// <summary>Per-agegroup pool detail. Empty and WAITLIST/Dropped agegroups are never reported.</summary>
public record ChecklistAgegroupPoolsDto
{
    public required Guid AgegroupId { get; init; }
    public required string AgegroupName { get; init; }
    /// <summary>Active teams still in the "Unassigned" holding division (or with no division).</summary>
    public required int UnpooledCount { get; init; }
    /// <summary>Total active teams in the agegroup.</summary>
    public required int TeamCount { get; init; }
}

/// <summary>Shape for steps whose unit of readiness is the agegroup (play dates).</summary>
public record ChecklistAgegroupStepDto
{
    public required bool Complete { get; init; }
    /// <summary>Names of schedulable agegroups still missing this configuration.</summary>
    public required List<string> MissingAgegroups { get; init; }
}

/// <summary>
/// Shape for steps whose unit of readiness is the division (field timeslots). Offenders are
/// grouped under their agegroup so the reason line reads "2030: Red, White" instead of a flat
/// list that runs to hundreds of entries on a large event.
/// </summary>
public record ChecklistDivisionStepDto
{
    public required bool Complete { get; init; }
    /// <summary>Total divisions still missing configuration, across all agegroups.</summary>
    public required int MissingDivisionCount { get; init; }
    public required List<ChecklistAgegroupDivisionsDto> MissingByAgegroup { get; init; }
}

/// <summary>An agegroup and the divisions under it that are still missing configuration.</summary>
public record ChecklistAgegroupDivisionsDto
{
    public required string AgegroupName { get; init; }
    public required List<string> DivNames { get; init; }
}

/// <summary>Build-rules readiness: divisions whose effective game guarantee is unset or zero.</summary>
public record ChecklistRulesStepDto
{
    public required bool Complete { get; init; }
    /// <summary>"Agegroup / Division" labels the engine would silently skip today.</summary>
    public required List<string> DivisionsWithoutGuarantee { get; init; }
}

/// <summary>Pairings status. Never gates the build — the engine generates missing pairings.</summary>
public record ChecklistPairingsStepDto
{
    public required bool Complete { get; init; }
    public required List<int> MissingPoolSizes { get; init; }
}
