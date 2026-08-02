namespace TSIC.Contracts.Dtos.JobClone;

// ══════════════════════════════════════
// Request
// ══════════════════════════════════════

public record JobCloneRequest
{
    public required Guid SourceJobId { get; init; }

    /// <summary>
    /// Customer that OWNS the new job. Normally the source's customer (the workbench seeds it from
    /// the picked source); pointing it elsewhere is the new-customer onboarding path — clone a
    /// template job, then retarget.
    ///
    /// MONEY PATH: Customers holds the ADN merchant credentials (AdnLoginId/AdnTransactionKey) and
    /// the payment path resolves them job → customer (GetJobAdnCredentials_FromJobId). Jobs.CustomerId
    /// IS the pointer to whose merchant account collects, so this is an explicit request field —
    /// never an accidental CopyScalars inheritance — and it participates in the plan fingerprint.
    /// </summary>
    public required Guid TargetCustomerId { get; init; }

    // Target identity
    public required string JobPathTarget { get; init; }
    public required string JobNameTarget { get; init; }
    public required string YearTarget { get; init; }
    public required string SeasonTarget { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>
    /// One rename row per source league (walked from Jobs.JobLeagues — ALL leagues clone).
    /// The workbench pre-fills one row per source league with a year-bumped name; the
    /// author edits before submit. Every source league must have a row.
    /// </summary>
    public List<LeagueRenameDto> Leagues { get; init; } = [];

    // Target dates
    public required DateTime ExpiryAdmin { get; init; }
    public required DateTime ExpiryUsers { get; init; }

    // ── Communications (all 8 parameters are request fields, pre-filled from source
    //    by the workbench and inline-editable — they land verbatim on the new job) ──
    public string? RegFormFrom { get; init; }
    public string? RegFormCcs { get; init; }
    public string? RegFormBccs { get; init; }
    public string? Rescheduleemaillist { get; init; }
    public string? Alwayscopyemaillist { get; init; }
    public string? MailTo { get; init; }
    public string? PayTo { get; init; }
    public string? StoreContactEmail { get; init; }

    /// <summary>
    /// Advance flag: year-bump names/content and shift DOB/GradYear by one year.
    /// Workbench defaults this to (yearDelta >= 1) — ON for next-year clones, OFF for
    /// same-year siblings.
    /// </summary>
    public bool UpAgegroupNamesByOne { get; init; }

    public bool NoParallaxSlide1 { get; init; }

    // ── LADT scope ──
    // "none" = no League/Agegroup/Division (configure post-release)
    // "lad"  = clone Leagues + Agegroups + Divisions (Teams re-register each season)
    // "ladt" = "lad" + clone Teams (structure teams only — see planner eligibility rule)
    public string LadtScope { get; init; } = "lad";

    /// <summary>
    /// Carry the source's divisions (pools) or start fresh. Mirrors
    /// <c>CloneAgegroupRequest.CopyDivisions</c> on the LADT agegroup clone, default and all.
    ///
    /// false = "clone the shape, not last year's pools": no pools carry and every cloned agegroup
    /// gets the single Unassigned holding division. Cloned structure teams land in it.
    ///
    /// Either way every cloned agegroup ends with an Unassigned division — that invariant is not
    /// optional (see JobCloneResetRules.CloneDivisions).
    /// </summary>
    public bool CopyDivisions { get; init; } = true;

    // ── eCheck enable flag ──
    // "off"    = BEnableEcheck=false on new job (recommended; admin re-opts in)
    // "source" = copy source.BEnableEcheck
    public string EnableEcheckChoice { get; init; } = "off";

    // ── Store ──
    // "keep"    = copy source.BEnableStore
    // "disable" = BEnableStore=false on new job (recommended — inventory never clones)
    public string StoreChoice { get; init; } = "disable";

    /// <summary>
    /// Data-moved guard: the PlanFingerprint from the ClonePlanDto the operator approved.
    /// The clone re-plans inside the transaction; if the fresh plan's fingerprint differs
    /// (source data changed since preview), the clone aborts with 409 + the fresh plan so
    /// the operator reviews what moved instead of silently cloning it.
    /// Null skips the guard (non-interactive callers only — the workbench always sends it).
    /// </summary>
    public string? PlanFingerprint { get; init; }
}

/// <summary>Per-league rename row (T3 multi-league walk).</summary>
public record LeagueRenameDto
{
    public required Guid SourceLeagueId { get; init; }
    public required string NameTarget { get; init; }
}

// ══════════════════════════════════════
// Response
// ══════════════════════════════════════

public record JobCloneResponse
{
    public required Guid NewJobId { get; init; }
    public required string NewJobPath { get; init; }
    public required string NewJobName { get; init; }

    /// <summary>
    /// Actual per-step created counts, in JobCloneStepOrder — same shape as the plan's
    /// Steps list so preview/clone parity is a row-by-row comparison.
    /// </summary>
    public required List<ClonePlanStepDto> Steps { get; init; }

    /// <summary>
    /// The Superuser RegistrationId on the new job for the user who executed the clone.
    /// The frontend uses this to call AuthService.selectRegistration() and re-mint the JWT
    /// scoped to the new job (only when the operator opted to enter the new job).
    /// Always populated — if the actor had no source Registration to clone, the service
    /// creates a fresh active Superuser Registration for them on the new job.
    /// </summary>
    public required Guid NewSuperUserRegistrationId { get; init; }
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
    /// Primary league name (first by BIsPrimary through JobLeagues). Null if the job has
    /// no league association. Display-only in the picker; the workbench's rename rows come
    /// from the plan's Leagues list, which walks ALL leagues.
    /// </summary>
    public string? LeagueName { get; init; }
}

/// <summary>
/// Response for the identity uniqueness check — flags whether the proposed
/// jobPath and/or jobName already exist on another job.
/// </summary>
public record IdentityExistsResponse
{
    public required bool PathExists { get; init; }
    public required bool NameExists { get; init; }
}

// ══════════════════════════════════════
// Clone plan (one plan, two consumers: preview renders it, execute re-plans
// inside the transaction and materializes it)
// ══════════════════════════════════════

public record ClonePlanDto
{
    /// <summary>Per-step planned counts in JobCloneStepOrder, with human notes.</summary>
    public required List<ClonePlanStepDto> Steps { get; init; }

    /// <summary>
    /// SHA-256 over the ordered step counts + source rowversion-ish inputs. The workbench
    /// echoes this back on JobCloneRequest.PlanFingerprint (data-moved guard).
    /// </summary>
    public required string PlanFingerprint { get; init; }

    public required int YearDelta { get; init; }

    /// <summary>Workbench default for the advance flag: yearDelta >= 1.</summary>
    public required bool AdvanceFlagDefault { get; init; }

    // Resolved fee rates (max(source, new-job floor)) — read-only display; no choices.
    public required decimal ResolvedProcessingFeePercent { get; init; }
    public decimal? SourceProcessingFeePercent { get; init; }
    public required decimal ResolvedEcheckProcessingFeePercent { get; init; }
    public decimal? SourceEcheckProcessingFeePercent { get; init; }

    public required bool SourceBEnableEcheck { get; init; }
    public required bool SourceBEnableStore { get; init; }

    /// <summary>
    /// What the new job's home-page banner will actually look like once the plan runs.
    /// Null when the source has no JobDisplayOptions row. See ClonedBannerPreviewDto.
    /// </summary>
    public ClonedBannerPreviewDto? BannerPreview { get; init; }

    // Communications defaults (source values — the workbench pre-fills its form fields).
    public string? RegFormFrom { get; init; }
    public string? RegFormCcs { get; init; }
    public string? RegFormBccs { get; init; }
    public string? Rescheduleemaillist { get; init; }
    public string? Alwayscopyemaillist { get; init; }
    public string? MailTo { get; init; }
    public string? PayTo { get; init; }
    public string? StoreContactEmail { get; init; }

    /// <summary>Source JobTypeId — drives workbench type-aware defaults (T9-A).</summary>
    public required int SourceJobTypeId { get; init; }

    /// <summary>
    /// The source job's owning customer. The workbench seeds its customer selector from this;
    /// picking a different one is the new-customer onboarding path.
    /// </summary>
    public required Guid SourceCustomerId { get; init; }

    /// <summary>
    /// True when the request's TargetCustomerId differs from the source's. Drives the cross-customer
    /// behaviors: source admin registrations are NOT copied, and the branding/billing/merchant-
    /// credential warnings below are raised.
    /// </summary>
    public required bool IsCrossCustomer { get; init; }

    // Jobs date shifts (by yearDelta, when present)
    public DateShiftDto? EventStartShift { get; init; }
    public DateShiftDto? EventEndShift { get; init; }
    public DateShiftDto? AdnArbStartShift { get; init; }
    public DateShiftDto? AdnStartDateAfterTrialShift { get; init; }
    public DateShiftDto? UslaxNumberValidThroughShift { get; init; }

    public required int AdminsToDeactivate { get; init; }
    public required int AdminsPreserved { get; init; }

    // Team eligibility breakdown (structure-vs-competing split; always populated so the
    // workbench can show it regardless of selected scope)
    public required int TeamsToClone { get; init; }
    public required int TeamsExcludedCompeting { get; init; }
    public required int TeamsExcludedWaitlistDropped { get; init; }
    public required int TeamsExcludedInactive { get; init; }

    /// <summary>One row per source league (T3) — feeds the workbench rename rows.</summary>
    public List<LeaguePlanDto> Leagues { get; init; } = [];

    public List<BulletinShiftDto> Bulletins { get; init; } = [];
    public List<AgegroupPreviewDto> Agegroups { get; init; } = [];
    public List<FeeModifierShiftDto> FeeModifiers { get; init; } = [];

    public List<string> Warnings { get; init; } = [];
}

public record ClonePlanStepDto
{
    /// <summary>A JobCloneStepOrder key.</summary>
    public required string StepKey { get; init; }
    public required int Count { get; init; }
    public string? Notes { get; init; }
}

public record LeaguePlanDto
{
    public required Guid SourceLeagueId { get; init; }
    public string? SourceName { get; init; }
    /// <summary>Year-bumped default target name (author-editable in the workbench).</summary>
    public string? DefaultNameTarget { get; init; }
    public required bool IsPrimary { get; init; }
    public required int AgegroupCount { get; init; }
    public required int DivisionCount { get; init; }
    public required int TeamCount { get; init; }
}

public record DateShiftDto
{
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
}

/// <summary>
/// The new job's home-page banner as it will stand the moment the clone lands — image
/// filenames and overlay text AFTER the year-bump and plain-banner options are applied.
/// The planner produces this by running the real reset rule, so it cannot drift from
/// what executes.
///
/// Image values are raw filenames ({sourceJobId}_paralaxbackgroundimage.jpg and friends,
/// per JobImageService's naming convention); the workbench resolves them through the same
/// buildAssetUrl helper the public banner uses. Text values are raw as stored — legacy
/// HTML-encoded or &lt;br&gt;-joined — and are decoded client-side by the shared overlay-text
/// helper, exactly as the public banner does.
/// </summary>
public record ClonedBannerPreviewDto
{
    /// <summary>parallaxSlideCount > 0 — the ONLY custom-banner switch the homesite reads.</summary>
    public required bool IsCustom { get; init; }

    public string? BackgroundImage { get; init; }
    public string? OverlayImage { get; init; }
    public string? Text1 { get; init; }
    public string? Text2 { get; init; }
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
// Release (site toggle + admin activation + verify + open registration)
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
/// Type-aware verify-then-release checklist (modern JobCloneQA): the cloned job's live
/// settings grouped into sections, ordered by relevance to the job's type. Rendered by
/// the release page's "Verify settings" panel before anything goes public.
/// </summary>
public record JobVerifyChecklistDto
{
    public required Guid JobId { get; init; }

    /// <summary>The job's URL segment — the release page builds configure deep-links from it.</summary>
    public required string JobPath { get; init; }
    public required string JobName { get; init; }
    public required int JobTypeId { get; init; }
    public string? JobTypeName { get; init; }

    /// <summary>Machine-readable release state — drives the release page's panel 2 button.</summary>
    public required bool BSuspendPublic { get; init; }

    /// <summary>Current registration-allow flags — seeds the open-registration panel.</summary>
    public required RegistrationFlagsDto RegistrationFlags { get; init; }

    public required List<VerifySectionDto> Sections { get; init; }
}

public record VerifySectionDto
{
    public required string Title { get; init; }
    public required List<VerifyItemDto> Items { get; init; }
}

public record VerifyItemDto
{
    public required string Label { get; init; }
    public required string Value { get; init; }

    /// <summary>
    /// Relative configure route (under the job's :jobPath prefix) where this setting is
    /// edited — the release page renders it as a deep-link. Null = display-only.
    /// </summary>
    public string? ConfigureRoute { get; init; }
}

/// <summary>
/// Open-registration action (release panel 4): which personas to open. All five
/// BRegistrationAllow* flags start FALSE on every clone; this is the deliberate flip.
/// </summary>
public record OpenRegistrationRequest
{
    public bool OpenPlayer { get; init; }
    public bool OpenTeam { get; init; }
    public bool OpenStaff { get; init; }
    public bool OpenReferee { get; init; }
    public bool OpenRecruiter { get; init; }
}

/// <summary>Current registration-allow flags after an open-registration action.</summary>
public record RegistrationFlagsDto
{
    public required Guid JobId { get; init; }
    public required bool AllowPlayer { get; init; }
    public required bool AllowTeam { get; init; }
    public required bool AllowStaff { get; init; }
    public required bool AllowReferee { get; init; }
    public required bool AllowRecruiter { get; init; }
}

// ══════════════════════════════════════
// Dev-only "Delete and return" — undo a clone in dev environment
// ══════════════════════════════════════

/// <summary>
/// Status payload for the "Delete and return" button on the release page.
/// CanUndo is true only when every safety predicate passes; Reasons enumerates
/// any blocking conditions when CanUndo is false. Counts drive the confirm modal
/// so the SuperUser can see the row impact before confirming.
/// </summary>
public record DevUndoStatusResponse
{
    public required bool CanUndo { get; init; }
    public required List<string> Reasons { get; init; }
    public required DevUndoCounts Counts { get; init; }
}

/// <summary>
/// Row counts surfaced to the confirm modal. Anything that should be 0 for a
/// fresh clone is broken out separately so the user sees which predicate failed.
/// </summary>
public record DevUndoCounts
{
    public required int AdminRegistrations { get; init; }
    public required int NonAdminRegistrations { get; init; }
    public required int RegistrationAccounting { get; init; }
    public required int Teams { get; init; }
    public required int JobFees { get; init; }
    public required int FeeModifiers { get; init; }
    public required int Bulletins { get; init; }
    public required int Agegroups { get; init; }
    public required int Divisions { get; init; }

    /// <summary>
    /// Sum of rows across every OTHER job-scoped table (generated by the EF model walk —
    /// any entity with a JobId property that isn't part of the clone manifest). Must be 0
    /// to undo. Breakdown lists each non-zero table by name.
    /// </summary>
    public required int AncillaryRows { get; init; }
    public List<string> AncillaryBreakdown { get; init; } = [];
}
