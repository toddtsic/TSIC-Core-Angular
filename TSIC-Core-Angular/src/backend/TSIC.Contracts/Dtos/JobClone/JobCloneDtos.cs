namespace TSIC.Contracts.Dtos.JobClone;

// ══════════════════════════════════════
// Request
// ══════════════════════════════════════

public record JobCloneRequest
{
    public required Guid SourceJobId { get; init; }

    // Target identity
    public required string JobPathTarget { get; init; }
    public required string JobNameTarget { get; init; }
    public required string YearTarget { get; init; }
    public required string SeasonTarget { get; init; }
    public required string DisplayName { get; init; }
    public required string LeagueNameTarget { get; init; }

    // Target dates
    public required DateTime ExpiryAdmin { get; init; }
    public required DateTime ExpiryUsers { get; init; }

    // Email
    public string? RegFormFrom { get; init; }

    // Flags
    public bool UpAgegroupNamesByOne { get; init; }

    // DEAD FIELD (kept for backward compat with existing frontend payloads).
    // Behavior is now unconditional and narrowed: Director + SuperDirector regs land
    // with BActive=false on every clone; Superuser regs are unchanged. The server
    // ignores whatever value is sent here. Remove from DTO after frontend is updated
    // (Phase D) to stop sending it.
    public bool SetDirectorsToInactive { get; init; }

    public bool NoParallaxSlide1 { get; init; }
}

// ══════════════════════════════════════
// Response
// ══════════════════════════════════════

public record JobCloneResponse
{
    public required Guid NewJobId { get; init; }
    public required string NewJobPath { get; init; }
    public required string NewJobName { get; init; }
    public required CloneSummary Summary { get; init; }
}

public record CloneSummary
{
    public int BulletinsCloned { get; init; }
    public int AgeRangesCloned { get; init; }
    public int MenusCloned { get; init; }
    public int MenuItemsCloned { get; init; }
    public int AdminRegistrationsCloned { get; init; }
    public int LeaguesCloned { get; init; }
    public int AgegroupsCloned { get; init; }
    public int DivisionsCloned { get; init; }
    public int FeesCloned { get; init; }
}

// ══════════════════════════════════════
// Source picker (for frontend dropdown)
// ══════════════════════════════════════

public record JobCloneSourceDto
{
    public required Guid JobId { get; init; }
    public required string JobPath { get; init; }
    public required string JobName { get; init; }
    public string? Year { get; init; }
    public string? Season { get; init; }
    public string? DisplayName { get; init; }
    public required Guid CustomerId { get; init; }

    /// <summary>
    /// Source league name (joined through JobLeagues). Null if the job has no league
    /// association. The wizard uses this to seed the leagueNameTarget default — stripping
    /// any year/season tokens so the author sees just the "name" portion to carry forward.
    /// </summary>
    public string? LeagueName { get; init; }
}

// ══════════════════════════════════════
// Blank-job creation (new-customer onboarding)
// ══════════════════════════════════════

/// <summary>
/// Create a brand-new empty job (no source). Used when onboarding a new customer that
/// has no prior job to clone from. Lands with the same safe-by-default state as a clone:
/// BSuspendPublic=true, BClubRepAllowEdit/Delete/Add=true, ProcessingFeePercent=current floor.
/// The author's own admin Registration is created with BActive=true (they need to work the job).
/// </summary>
public record BlankJobRequest
{
    public required Guid CustomerId { get; init; }

    // Target identity
    public required string JobPathTarget { get; init; }
    public required string JobNameTarget { get; init; }
    public required string YearTarget { get; init; }
    public required string SeasonTarget { get; init; }
    public required string DisplayName { get; init; }

    // Target dates
    public required DateTime ExpiryAdmin { get; init; }
    public required DateTime ExpiryUsers { get; init; }

    // Required FKs on Jobs (wizard picks defaults in Step 1/2)
    public required int BillingTypeId { get; init; }
    public required int JobTypeId { get; init; }
    public required Guid SportId { get; init; }

    // Email
    public string? RegFormFrom { get; init; }
}

public record BlankJobResponse
{
    public required Guid NewJobId { get; init; }
    public required string NewJobPath { get; init; }
    public required string NewJobName { get; init; }
}

// ══════════════════════════════════════
// Clone preview (dry-run transforms; no writes)
// ══════════════════════════════════════

/// <summary>
/// Preview the transforms a clone will perform without committing. The author uses this
/// to verify year-delta shifts and name inference before submitting.
/// Reuses JobCloneRequest as input (same fields the real clone needs).
/// </summary>
public record JobClonePreviewResponse
{
    public required int YearDelta { get; init; }
    public required string InferredLeagueName { get; init; }
    public required decimal CurrentProcessingFeePercent { get; init; }
    public decimal? SourceProcessingFeePercent { get; init; }

    public DateShiftDto? EventStartShift { get; init; }
    public DateShiftDto? EventEndShift { get; init; }
    public DateShiftDto? AdnArbStartShift { get; init; }

    public required int AdminsToDeactivate { get; init; }
    public required int AdminsPreserved { get; init; }

    public List<BulletinShiftDto> Bulletins { get; init; } = [];
    public List<AgegroupPreviewDto> Agegroups { get; init; } = [];
    public List<FeeModifierShiftDto> FeeModifiers { get; init; } = [];
}

public record DateShiftDto
{
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
}

public record BulletinShiftDto
{
    public required Guid SourceBulletinId { get; init; }
    public string? Title { get; init; }
    public DateShiftDto? CreateDate { get; init; }
    public DateShiftDto? StartDate { get; init; }
    public DateShiftDto? EndDate { get; init; }
}

public record AgegroupPreviewDto
{
    public required Guid SourceAgegroupId { get; init; }
    public string? SourceName { get; init; }
    public string? NewName { get; init; }
    public int? SourceGradYearMin { get; init; }
    public int? NewGradYearMin { get; init; }
    public int? SourceGradYearMax { get; init; }
    public int? NewGradYearMax { get; init; }
    public DateShiftDto? DobMin { get; init; }
    public DateShiftDto? DobMax { get; init; }
    public DateShiftDto? DiscountFeeStart { get; init; }
    public DateShiftDto? DiscountFeeEnd { get; init; }
    public DateShiftDto? LateFeeStart { get; init; }
    public DateShiftDto? LateFeeEnd { get; init; }
}

public record FeeModifierShiftDto
{
    public required Guid SourceFeeModifierId { get; init; }
    public required string ModifierType { get; init; }
    public required decimal Amount { get; init; }
    public DateShiftDto? StartDate { get; init; }
    public DateShiftDto? EndDate { get; init; }
}

// ══════════════════════════════════════
// Release (site toggle + admin activation)
// ══════════════════════════════════════

/// <summary>
/// Request to activate a set of admin registrations on a suspended job.
/// Each registrationId must belong to the target job; otherwise the call fails with 403.
/// </summary>
public record ReleaseAdminsRequest
{
    public required List<Guid> RegistrationIds { get; init; }
}

/// <summary>
/// Summary of an admin registration on a suspended job — used to populate the
/// activation panel on the Release screen.
/// </summary>
public record ReleasableAdminDto
{
    public required Guid RegistrationId { get; init; }
    public required string RoleId { get; init; }
    public string? RoleName { get; init; }
    public string? UserId { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public required bool BActive { get; init; }
}

/// <summary>
/// Minimal payload returned after a release action — reflects the new state.
/// </summary>
public record ReleaseResponse
{
    public required Guid JobId { get; init; }
    public required bool BSuspendPublic { get; init; }
    public required int AdminsActivated { get; init; }
}

/// <summary>
/// Suspended job for the Landing list — the author clicks through to the Release mode.
/// </summary>
public record SuspendedJobDto
{
    public required Guid JobId { get; init; }
    public required string JobPath { get; init; }
    public required string JobName { get; init; }
    public string? Year { get; init; }
    public string? Season { get; init; }
    public string? DisplayName { get; init; }
    public required Guid CustomerId { get; init; }
    public DateTime? Modified { get; init; }
    public required int InactiveAdminCount { get; init; }
}
