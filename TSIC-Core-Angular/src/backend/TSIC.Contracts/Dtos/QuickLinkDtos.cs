namespace TSIC.Contracts.Dtos;

// ══════════════════════════════════════════════════════════════════════════
// QuickLinks — SuperUser-controlled landing-hero links.
//
// Two tables back this: quicklinks.LinkType (global catalog) and
// quicklinks.JobQuickLink (per-job overrides). The landing hero resolves each
// link's on/off from the job PULSE (one grounding implementation), so these
// DTOs carry config only — never a server-resolved on/off. See QuickLinksService.
// ══════════════════════════════════════════════════════════════════════════

/// <summary>
/// One resolved quicklink for the public landing hero (embedded on JobPulseDto).
/// The full active catalog is emitted as the Option-A baseline; the client makes
/// the final visible/hidden call using the pulse + the per-job override below.
/// </summary>
public record QuickLinkResolvedDto
{
    public required string LinkKey { get; init; }

    /// <summary>Effective label = per-job override ?? catalog default.</summary>
    public required string Label { get; init; }

    public string? Icon { get; init; }
    public string? RouteTemplate { get; init; }
    public string? NavigateUrl { get; init; }
    public string? Target { get; init; }

    /// <summary>Effective order = per-job override ?? catalog default.</summary>
    public required int SortOrder { get; init; }

    /// <summary>
    /// Name of the camelCase JobPulse field the client reads to decide grounded
    /// on/off (informational; the landing hero's own predicate is the real gate).
    /// Null = ungrounded ("deliberate-on": shown only when <see cref="Enabled"/> is true).
    /// </summary>
    public string? GroundingPulseFlag { get; init; }

    public required bool GroundingInverted { get; init; }

    /// <summary>
    /// Per-job override (JobQuickLink.Enabled). For grounded links: null = follow
    /// grounding, false = SuperUser force-hide (true never persisted). For ungrounded
    /// links: true = force-show, null/false = hidden.
    /// </summary>
    public bool? Enabled { get; init; }
}

/// <summary>One editor grid row — one per active catalog LinkType for a chosen job.</summary>
public record QuickLinkEditorRowDto
{
    public required string LinkKey { get; init; }
    public required string DefaultLabel { get; init; }

    /// <summary>JobQuickLink.Label override; null = use <see cref="DefaultLabel"/>.</summary>
    public string? OverrideLabel { get; init; }

    public required bool IsGrounded { get; init; }

    /// <summary>Raw Jobs column the catalog grounds on (display only).</summary>
    public string? GroundingSetting { get; init; }

    /// <summary>Mapped camelCase pulse flag the editor reads to preview grounded on/off.</summary>
    public string? GroundingPulseFlag { get; init; }

    public required bool GroundingInverted { get; init; }
    public required int DefaultSortOrder { get; init; }
    public int? OverrideSortOrder { get; init; }
    public required int EffectiveSortOrder { get; init; }

    public string? Icon { get; init; }
    public string? RouteTemplate { get; init; }
    public string? NavigateUrl { get; init; }
    public string? Target { get; init; }

    /// <summary>
    /// JobQuickLink.Enabled override (null = follow grounding / ungrounded-off,
    /// false = force-hide, true = ungrounded force-show).
    /// </summary>
    public bool? Enabled { get; init; }

    /// <summary>True when a JobQuickLink row exists for this (job, linkKey).</summary>
    public required bool HasJobRow { get; init; }
}

/// <summary>Editor model for one chosen job (catalog rows + the job's overrides).</summary>
public record QuickLinkEditorModelDto
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
    public required string JobPath { get; init; }
    public required List<QuickLinkEditorRowDto> Rows { get; init; }
}

/// <summary>Batch save — upserts/deletes JobQuickLink rows for one job.</summary>
public record SaveQuickLinksRequest
{
    public required List<QuickLinkSaveRowDto> Rows { get; init; }
}

public record QuickLinkSaveRowDto
{
    public required string LinkKey { get; init; }

    /// <summary>True = delete any JobQuickLink row (revert to pure catalog default).</summary>
    public required bool Delete { get; init; }

    /// <summary>
    /// Ungrounded: true = force-show. Grounded: false = force-hide, null = follow.
    /// The server clamps grounded links to {null, false} regardless of what is sent.
    /// </summary>
    public bool? Enabled { get; init; }

    /// <summary>Label override; null = clear (use catalog default).</summary>
    public string? Label { get; init; }

    /// <summary>Sort override; null = clear (use catalog default).</summary>
    public int? SortOrder { get; init; }
}
