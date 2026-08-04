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

    /// <summary>Step 2 — every schedulable agegroup has at least one play date.</summary>
    public required ChecklistAgegroupStepDto Dates { get; init; }

    /// <summary>Step 3 — every schedulable agegroup has at least one field assignment.</summary>
    public required ChecklistAgegroupStepDto Fields { get; init; }

    /// <summary>Step 4 — every schedulable division resolves a game guarantee &gt; 0.</summary>
    public required ChecklistRulesStepDto Rules { get; init; }

    /// <summary>Step 5 — informational: missing pairings are auto-generated at build.</summary>
    public required ChecklistPairingsStepDto Pairings { get; init; }

    /// <summary>Scheduled games currently on the books for this job.</summary>
    public required int GameCount { get; init; }

    /// <summary>True when steps 1–4 are all complete — the build may proceed.</summary>
    public required bool BuildUnlocked { get; init; }
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

/// <summary>Shared shape for steps whose unit of readiness is the agegroup (dates, fields).</summary>
public record ChecklistAgegroupStepDto
{
    public required bool Complete { get; init; }
    /// <summary>Names of schedulable agegroups still missing this configuration.</summary>
    public required List<string> MissingAgegroups { get; init; }
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
