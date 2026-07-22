using FluentValidation;
using TSIC.Domain.Constants;

namespace TSIC.Contracts.Dtos;

public sealed record ClubRepClubDto
{
    public required string ClubName { get; init; }
    public required bool IsInUse { get; init; }
}

public sealed record InitializeRegistrationRequest
{
    public required string ClubName { get; init; }
    public required string JobPath { get; init; }
    /// <summary>Signed invite token from the invitation link (?invite=). Required only for token-gated events.</summary>
    public string? InviteToken { get; init; }
}

public class InitializeRegistrationRequestValidator : AbstractValidator<InitializeRegistrationRequest>
{
    public InitializeRegistrationRequestValidator()
    {
        RuleFor(x => x.ClubName)
            .NotEmpty().WithMessage("Club name is required");

        RuleFor(x => x.JobPath)
            .NotEmpty().WithMessage("Job path is required");
    }
}

public sealed record AddClubToRepRequest
{
    public required string ClubName { get; init; }
}

public class AddClubToRepRequestValidator : AbstractValidator<AddClubToRepRequest>
{
    public AddClubToRepRequestValidator()
    {
        RuleFor(x => x.ClubName)
            .NotEmpty().WithMessage("Club name is required")
            .MaximumLength(200).WithMessage("Club name cannot exceed 200 characters");
    }
}

public sealed record RemoveClubFromRepRequest
{
    public required string ClubName { get; init; }
}

public class RemoveClubFromRepRequestValidator : AbstractValidator<RemoveClubFromRepRequest>
{
    public RemoveClubFromRepRequestValidator()
    {
        RuleFor(x => x.ClubName)
            .NotEmpty().WithMessage("Club name is required");
    }
}

public sealed record UpdateClubNameRequest
{
    public required string OldClubName { get; init; }
    public required string NewClubName { get; init; }
}

public class UpdateClubNameRequestValidator : AbstractValidator<UpdateClubNameRequest>
{
    public UpdateClubNameRequestValidator()
    {
        RuleFor(x => x.OldClubName)
            .NotEmpty().WithMessage("Old club name is required")
            .MaximumLength(200).WithMessage("Club name cannot exceed 200 characters");

        RuleFor(x => x.NewClubName)
            .NotEmpty().WithMessage("New club name is required")
            .MaximumLength(200).WithMessage("Club name cannot exceed 200 characters");
    }
}

public sealed record TeamsMetadataResponse
{
    public required int ClubId { get; init; }
    public required string ClubName { get; init; }
    public required List<ClubTeamDto> ClubTeams { get; init; }
    public required List<SuggestedTeamNameDto> SuggestedTeamNames { get; init; }
    public required List<RegisteredTeamDto> RegisteredTeams { get; init; }
    // Teams the rep entered for this event that a director later moved into a "DROPPED"
    // age group. Read-only history surfaced as a separate muted bucket in the wizard's
    // club library — never offered for re-registration. Same shape as RegisteredTeams.
    public required List<RegisteredTeamDto> DroppedTeams { get; init; }
    public required List<AgeGroupDto> AgeGroups { get; init; }
    public required bool BPayBalanceDue { get; init; }
    public required bool BTeamsFullPaymentRequired { get; init; }
    public string? PlayerRegRefundPolicy { get; init; }
    public required int PaymentMethodsAllowedCode { get; init; }
    public required bool BAddProcessingFees { get; init; }
    public required bool BApplyProcessingFeesToTeamDeposit { get; init; }
    public required bool HasActiveDiscountCodes { get; init; }
    /// <summary>True iff this job's merchant account accepts AMEX (see IJobPaymentFeaturesService).
    /// Gates whether the team payment step offers AMEX as a card type. Fail-closed false.</summary>
    public required bool JobUsesAmex { get; init; }
    public UserContactInfoDto? ClubRepContactInfo { get; init; }
    public string? PayTo { get; init; }
    public string? MailTo { get; init; }
    public string? MailinPaymentWarning { get; init; }
    public required List<string> LopOptions { get; init; }
    public required bool BWaiverSigned3 { get; init; }
    // Per-job opt-in for eCheck (ACH) as a customer-facing payment method.
    // When true, the team checkout shows the Pay-by-eCheck option alongside CC.
    public required bool BEnableEcheck { get; init; }
    // ARB-Trial schedule. When AdnArbTrial=true, the wizard renders the
    // deposit-tomorrow + balance-on-AdnStartDateAfterTrial flow; otherwise the
    // flow runs unchanged. Dates are job-config and read at submit time.
    public bool? AdnArbTrial { get; init; }
    public DateTime? AdnArbStartDate { get; init; }
    public DateTime? AdnStartDateAfterTrial { get; init; }
    // Per-job opt-in: offer an optional donation field on the team payment page.
    public required bool BIncludeTeamDonation { get; init; }
    // Effective (clamped) processing-fee rates as decimal multipliers (e.g. 0.035 = 3.5%).
    // The wizard multiplies a freely-typed donation by these to reprice proc client-side; they
    // are the SAME rates the server charges, so the team payment's amount tripwire stays quiet.
    public required decimal EffectiveProcessingRate { get; init; }
    public required decimal EffectiveEcheckProcessingRate { get; init; }
}

public sealed record ClubTeamDto
{
    public required int ClubTeamId { get; init; }
    public required string ClubTeamName { get; init; }
    public required string ClubTeamGradYear { get; init; }
    public required string ClubTeamLevelOfPlay { get; init; }
    // True if this ClubTeam has ever appeared on a schedule (any job). When true,
    // name/gradYear/LOP are locked to protect the team's historical performance record.
    public required bool BHasBeenScheduled { get; init; }
    // True when the rep has retired the team from their visible library. Archived rows
    // still reserve (name, gradYear) so historical performance records stay attributable.
    public required bool BArchived { get; init; }
}

public sealed record CreateClubTeamRequest
{
    public required string ClubTeamName { get; init; }
    public required string ClubTeamGradYear { get; init; }
    public string? LevelOfPlay { get; init; }
}

public sealed record UpdateClubTeamRequest
{
    public required string ClubTeamName { get; init; }
    public required string ClubTeamGradYear { get; init; }
    public required string ClubTeamLevelOfPlay { get; init; }
}

public sealed record SuggestedTeamNameDto
{
    public required string TeamName { get; init; }
    public required int UsageCount { get; init; }
    public required int Year { get; init; }
}

public sealed record RegisteredTeamDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required Guid AgeGroupId { get; init; }
    public required string AgeGroupName { get; init; }
    // Waitlist status + clean display name, resolved ONCE here from the minted
    // "WAITLIST - {agegroup}" mirror name (AgegroupConstants) — the single load-bearing
    // signal. Every consumer (teams / payment / family-payment grids, club library fly-in)
    // reads these instead of re-parsing the prefix string, so the WL indicator can no longer
    // drift between views or vanish when a screen hides the age-group column (PL-037).
    public bool IsWaitlisted => AgegroupConstants.IsWaitlist(AgeGroupName);
    public string AgeGroupDisplayName => AgegroupConstants.StripWaitlistPrefix(AgeGroupName);
    public required string? LevelOfPlay { get; init; }
    public int? ClubTeamId { get; init; }
    // Mirrors ClubTeamDto.BHasBeenScheduled — true if the team's ClubTeamId has ever
    // appeared on a schedule. Used client-side to gate the edit pencil on entered rows.
    public required bool BHasBeenScheduled { get; init; }
    public required decimal FeeBase { get; init; }
    // Statement-of-fact: raw value from Teams.FeeProcessing. Lifetime CC proc target,
    // decremented only by non-CC payment events. Carried as-is for ledger consumers.
    // For "what proc fee is charged if the rest is paid by CC right now" use
    // FeeProcessingDue, which is computed at the display boundary.
    public required decimal FeeProcessing { get; init; }
    // Display semantic for the payment grid's "ProcFee Due" column.
    // Defined as OwedTotal − CkOwedTotal (CC-billable total minus check-billable total
    // = the CC processing fee component owed right now). Equals FeeProcessing under
    // healthy ledger invariants, but expressed here in display terms so consumers
    // don't reach into the statement-of-fact field for display.
    public required decimal FeeProcessingDue { get; init; }
    public required decimal FeeDiscount { get; init; }
    public required decimal FeeLatefee { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }
    // ── Canonical display decomposition (PaymentState) — corrections are adjustments, not tender ──
    // FeeAdj: signed net adjustment shown in the unified "Fee-Adj" column = lateFee − discount −
    // correction (late fee / correction-charge positive; discount / correction-credit negative).
    // Folds in what used to be the separate Discount column.
    public required decimal FeeAdj { get; init; }
    // TenderPaid: real money received (CC + eCheck + check + cash) — the "Paid" column. Excludes
    // Correction-method rows, which now surface in FeeAdj. OwedTotal is unchanged either way.
    public required decimal TenderPaid { get; init; }
    // Immutable fee structure — what the team was committed to, independent of payment state.
    // Used by the Teams step to surface phase-agnostic team properties.
    public required decimal Deposit { get; init; }
    public required decimal BalanceDue { get; init; }
    // Per-scope payment phase (canonical ResolveFullPaymentPhase: a JobFees.BFullPaymentRequired
    // override team→agegroup→league wins, else Jobs.BTeamsFullPaymentRequired). True → this team
    // owes its full price now (balance active); false → deposit phase. Per-TEAM, not a cart-wide
    // flag — a club-rep cart can span scopes that differ in phase, so the payment grid drives the
    // "Balance Due" column and the phase badge from THIS, not the job-level setting.
    public required bool FullPaymentRequired { get; init; }
    // Net-of-paid ledger state — used by the Payment step.
    public required decimal DepositDue { get; init; }
    public required decimal AdditionalDue { get; init; }
    public required DateTime RegistrationTs { get; init; }
    public required bool BWaiverSigned3 { get; init; }
    public required decimal CcOwedTotal { get; init; }
    public required decimal CkOwedTotal { get; init; }
    // eCheck-billable total: CcOwedTotal minus the (CC − eCheck) proc credit. Lower than
    // CcOwedTotal (eCheck proc rate < CC) and higher than CkOwedTotal (eCheck still
    // collects some proc). The rep must be shown + submit THIS when paying by eCheck —
    // the charge engine debits the same figure (see PaymentRateMath.AppliedProcCredit).
    public required decimal EkOwedTotal { get; init; }
    // Team active state. False for waitlisted/dropped/inactive teams — used by the
    // director's club-rep accounting grid to split active teams (counted in totals)
    // from the muted waitlist/dropped/inactive bucket. Always true on the rep's own
    // payment page (only active teams are surfaced there).
    public required bool Active { get; init; }
    // True when this team has an active ARB subscription with a charge still scheduled
    // in the future. Drives the "auto-pay scheduled" calendar badge on the Owed column.
    public required bool PaymentScheduled { get; init; }
    // Next scheduled ARB charge date (informational — badge tooltip). Null when not scheduled.
    public DateTime? NextChargeDate { get; init; }
}

public sealed record AgeGroupDto
{
    public required Guid AgeGroupId { get; init; }
    public required string AgeGroupName { get; init; }
    public required int MaxTeams { get; init; }
    public required int RegisteredCount { get; init; }
    public required decimal Deposit { get; init; }
    public required decimal BalanceDue { get; init; }
}

public sealed record RegisterTeamRequest
{
    public int? ClubTeamId { get; init; }
    public string? TeamName { get; init; }
    public string? ClubTeamGradYear { get; init; }
    public required Guid AgeGroupId { get; init; }
    public string? LevelOfPlay { get; init; }
}

public class RegisterTeamRequestValidator : AbstractValidator<RegisterTeamRequest>
{
    public RegisterTeamRequestValidator()
    {
        RuleFor(x => x.AgeGroupId)
            .NotEmpty().WithMessage("Age group is required");

        RuleFor(x => x)
            .Must(x => x.ClubTeamId.HasValue || !string.IsNullOrWhiteSpace(x.TeamName))
            .WithMessage("Either select an existing team or provide a new team name");

        RuleFor(x => x.TeamName)
            .MaximumLength(80).WithMessage("Team name cannot exceed 80 characters")
            .When(x => !string.IsNullOrWhiteSpace(x.TeamName));

        RuleFor(x => x.ClubTeamGradYear)
            .NotEmpty().WithMessage("Graduation year is required when creating a new team")
            .When(x => !x.ClubTeamId.HasValue);

        RuleFor(x => x.LevelOfPlay)
            .NotEmpty().WithMessage("Level of play is required when creating a new team")
            .When(x => !x.ClubTeamId.HasValue);
    }
}

public sealed record RegisterTeamResponse
{
    public required Guid TeamId { get; init; }
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public bool IsWaitlisted { get; init; }
    public string? WaitlistAgegroupName { get; init; }
}



public sealed record AddClubToRepResponse
{
    public required bool Success { get; init; }
    public required string ClubName { get; init; }
    public List<ClubSearchResult>? SimilarClubs { get; init; }
    public string? Message { get; init; }
}

public sealed record ValidateClubRepRequest
{
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required string JobPath { get; init; }
}

public sealed record ValidateClubRepResponse
{
    public required bool IsValid { get; init; }
    public required string? ClubName { get; init; }
    public string? Message { get; init; }
}
public sealed record CheckExistingRegistrationsResponse
{
    public required bool HasConflict { get; init; }
    public string? OtherRepUsername { get; init; }
    public int TeamCount { get; init; }
}

public sealed record RecalculateTeamFeesRequest
{
    public Guid? JobId { get; init; }
    public Guid? TeamId { get; init; }
}

public class RecalculateTeamFeesRequestValidator : AbstractValidator<RecalculateTeamFeesRequest>
{
    public RecalculateTeamFeesRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => (x.JobId.HasValue && !x.TeamId.HasValue) || (!x.JobId.HasValue && x.TeamId.HasValue))
            .WithMessage("Exactly one of JobId or TeamId must be provided");
    }
}

public sealed record RecalculateTeamFeesResponse
{
    public required int UpdatedCount { get; init; }
    public required List<TeamFeeUpdateDto> Updates { get; init; }
    public required int SkippedCount { get; init; }
    public required List<string> SkippedReasons { get; init; }
}

public sealed record TeamFeeUpdateDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required string AgeGroupName { get; init; }
    public required decimal OldFeeBase { get; init; }
    public required decimal NewFeeBase { get; init; }
    public required decimal OldFeeProcessing { get; init; }
    public required decimal NewFeeProcessing { get; init; }
    public required string UpdatedBy { get; init; }
    public required DateTime UpdatedAt { get; init; }
}
