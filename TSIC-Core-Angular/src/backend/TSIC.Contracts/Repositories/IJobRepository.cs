using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public record JobPreSubmitMetadata
{
    public string? PlayerProfileMetadataJson { get; init; }
    public string? JsonOptions { get; init; }
    public string? CoreRegformPlayer { get; init; }

    /// <summary>
    /// True when Jobs.CoreRegformPlayer contains the ALLOWPIF token.
    /// Gates whether the player checkout exposes the Pay In Full option.
    /// </summary>
    public bool AllowPif { get; init; }

    /// <summary>
    /// Job-level phase flag. When true, every active player registration's FeeBase
    /// is stamped at Deposit + BalanceDue (full payment phase). When false, FeeBase
    /// is stamped at Deposit (deposit phase). Director-controlled.
    /// </summary>
    public bool BPlayersFullPaymentRequired { get; init; }
}

public record JobPaymentInfo
{
    public bool? AdnArb { get; init; }
    public int? AdnArbbillingOccurences { get; init; }
    public int? AdnArbintervalLength { get; init; }
    public DateTime? AdnArbstartDate { get; init; }

    /// <summary>
    /// True when Jobs.CoreRegformPlayer contains the ALLOWPIF token.
    /// Gates whether the payment service accepts PaymentOption.PIF.
    /// </summary>
    public bool AllowPif { get; init; }

    /// <summary>
    /// Job-level phase flag. When true, every active player registration's FeeBase
    /// is stamped at Deposit + BalanceDue (full payment phase). PaymentService also
    /// accepts PaymentOption.PIF when this flag is true, regardless of AllowPif.
    /// </summary>
    public bool BPlayersFullPaymentRequired { get; init; }

    /// <summary>
    /// True when the job admin has opted in to eCheck (ACH) as a customer-facing
    /// payment method. Gates whether PaymentService accepts BankAccount-bearing requests.
    /// </summary>
    public bool BEnableEcheck { get; init; }
}

public record JobMetadata
{
    public string? PlayerProfileMetadataJson { get; init; }
    public string? JsonOptions { get; init; }
    public string? CoreRegformPlayer { get; init; }
}

public record JobMetadataDto
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
    public required string JobPath { get; init; }
    public string? JobLogoPath { get; init; }
    public string? JobBannerPath { get; init; }
    public string? JobBannerText1 { get; init; }
    public string? JobBannerText2 { get; init; }
    public string? JobBannerBackgroundPath { get; init; }
    public bool? CoreRegformPlayer { get; init; }
    public string? CoreRegformPlayerRaw { get; init; }
    public DateTime? USLaxNumberValidThroughDate { get; init; }
    public DateTime? ExpiryUsers { get; init; }
    public string? PlayerProfileMetadataJson { get; init; }
    public string? JsonOptions { get; init; }
    public string? MomLabel { get; init; }
    public string? DadLabel { get; init; }
    public string? PlayerRegReleaseOfLiability { get; init; }
    public string? PlayerRegCodeOfConduct { get; init; }
    public string? PlayerRegCovid19Waiver { get; init; }
    public string? PlayerRegRefundPolicy { get; init; }
    public required bool OfferPlayerRegsaverInsurance { get; init; }
    public required bool BOfferTeamRegsaverInsurance { get; init; }
    public bool? AdnArb { get; init; }
    public int? AdnArbBillingOccurences { get; init; }
    public int? AdnArbIntervalLength { get; init; }
    public DateTime? AdnArbStartDate { get; init; }
    public required bool BRegistrationAllowPlayer { get; init; }
    public required bool BRegistrationAllowTeam { get; init; }
    public required bool BEnableStore { get; init; }
    public required bool BScheduleAllowPublicAccess { get; init; }
    public required bool BBannerIsCustom { get; init; }
    public string? JobTypeName { get; init; }
    /// <summary>Canonical job type discriminator — see <see cref="TSIC.Domain.Constants.JobConstants"/>.</summary>
    public required int JobTypeId { get; init; }
    public string? SportName { get; init; }
    public required int PaymentMethodsAllowedCode { get; init; }
    public required bool BAddProcessingFees { get; init; }
    public string? PayTo { get; init; }
    public string? MailTo { get; init; }
    public string? MailinPaymentWarning { get; init; }

    /// <summary>
    /// Per-job opt-in for eCheck (ACH) as a customer-facing payment method. When true,
    /// the player and team checkout flows expose the Pay-by-eCheck UI alongside CC.
    /// </summary>
    public required bool BEnableEcheck { get; init; }

    /// <summary>
    /// True when Jobs.CoreRegformPlayer contains the ALLOWPIF token.
    /// Gates whether the player checkout exposes the Pay In Full option.
    /// </summary>
    public required bool AllowPif { get; init; }

    /// <summary>
    /// Job-level phase flag. When true, every active player registration's FeeBase
    /// is stamped at Deposit + BalanceDue (full payment phase). Drives wizard display
    /// defaults and director-controlled balance-due workflow.
    /// </summary>
    public required bool BPlayersFullPaymentRequired { get; init; }

    /// <summary>Per-job opt-in: offer an optional donation field on the player payment page.</summary>
    public required bool BIncludePlayerDonation { get; init; }

    /// <summary>Per-job opt-in: offer an optional donation field on the team payment page.</summary>
    public required bool BIncludeTeamDonation { get; init; }
}

/// <summary>
/// The two job-level full-payment phase baselines used as the fallback when no per-scope
/// JobFees override exists (mirrors the jobBaseline arg of ResolvedFee.ResolveFullPaymentPhase).
/// Players flag → Player role; Teams flag → ClubRep role.
/// </summary>
public record JobFullPaymentBaseline
{
    public required bool BPlayersFullPaymentRequired { get; init; }
    public required bool BTeamsFullPaymentRequired { get; init; }
}

public record JobRegistrationStatus
{
    public required bool BRegistrationAllowPlayer { get; init; }
    public required bool BPlayerRegRequiresToken { get; init; }
    public required bool BRegistrationAllowTeam { get; init; }
    public required bool BTeamRegRequiresToken { get; init; }
    public DateTime? ExpiryUsers { get; init; }
}

public record PriorYearJobInfo
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
    public required string Year { get; init; }
}

/// <summary>
/// Repository for managing Jobs entity data access.
/// </summary>
public interface IJobRepository
{
    /// <summary>
    /// Fetch minimal metadata needed for player pre-submit.
    /// </summary>
    Task<JobPreSubmitMetadata?> GetPreSubmitMetadataAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch payment configuration for a job (ARB settings).
    /// </summary>
    Task<JobPaymentInfo?> GetJobPaymentInfoAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch the two job-level full-payment phase baselines (players + teams) in one query.
    /// Used by the LADT tree so the grid's Payment Phase column can resolve like the backend.
    /// </summary>
    Task<JobFullPaymentBaseline?> GetFullPaymentBaselineAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch job metadata fields (PlayerProfileMetadataJson, JsonOptions, CoreRegformPlayer).
    /// </summary>
    Task<JobMetadata?> GetJobMetadataAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find job by JobPath (case-insensitive).
    /// </summary>
    Task<Guid?> GetJobIdByPathAsync(string jobPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get job URL path by JobId. Used to construct invite links.
    /// </summary>
    Task<string?> GetJobPathAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get job registration status (is player registration active).
    /// </summary>
    Task<JobRegistrationStatus?> GetRegistrationStatusAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Canonical "is this job expired for non-admin users" determination:
    /// <c>DateTime.Now &gt;= Jobs.ExpiryUsers</c>. This is the single source of truth for the
    /// public/club-rep expiry boundary — every non-admin write gate should consult it rather
    /// than comparing <c>ExpiryUsers</c> inline. Admins use the separate <c>ExpiryAdmin</c>
    /// boundary (enforced on the read side). Returns true for an unknown jobId (fail closed).
    /// </summary>
    Task<bool> IsJobExpiredForUsersAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Canonical "has this event concluded" determination — the CREATE door (distinct from the
    /// <see cref="IsJobExpiredForUsersAsync"/> login/expiry door). Applies the single shared
    /// predicate <c>TSIC.Domain.JobRules.JobLifecycle.EventConcluded</c> over the fact hierarchy
    /// (published last-game date → EventEndDate → ExpiryUsers fallback). Returns true for an
    /// unknown jobId (fail closed). Use when a create surface needs the over/not-over verdict
    /// without the full capability composition.
    /// </summary>
    Task<bool> IsEventConcludedAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get full job metadata with display options for frontend rendering.
    /// </summary>
    Task<JobMetadataDto?> GetJobMetadataByPathAsync(string jobPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get insurance offer info for VerticalInsure.
    /// </summary>
    Task<InsuranceOfferInfo?> GetInsuranceOfferInfoAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get job confirmation info for on-screen display.
    /// </summary>
    Task<JobConfirmationInfo?> GetConfirmationInfoAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get job confirmation info for email.
    /// </summary>
    Task<JobConfirmationEmailInfo?> GetConfirmationEmailInfoAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get job basic info for team registration initialization (JobId, JobPath, LogoHeader).
    /// </summary>
    Task<JobAuthInfo?> GetJobAuthInfoAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get job fee settings for team registration metadata.
    /// Returns BTeamsFullPaymentRequired, BAddProcessingFees, BApplyProcessingFeesToTeamDeposit, PaymentMethodsAllowedCode, PlayerRegRefundPolicy.
    /// </summary>
    Task<JobFeeSettings?> GetJobFeeSettingsAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get job season by job ID for team queries.
    /// </summary>
    Task<string?> GetJobSeasonAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get job season and year for LADT entity creation.
    /// </summary>
    Task<JobSeasonYear?> GetJobSeasonYearAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get job name by job ID.
    /// </summary>
    Task<string?> GetJobNameAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get customer ID for a job.
    /// </summary>
    Task<Guid?> GetCustomerIdAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the job's customer (merchant account) is configured to accept American Express
    /// (Jobs.Customers.bAllowAmex). Fail-closed: false when the job or its customer is absent.
    /// </summary>
    Task<bool> GetCustomerUsesAmexAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if job uses waitlists.
    /// </summary>
    Task<bool> GetUsesWaitlistsAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if public schedule/roster access is enabled for a job.
    /// </summary>
    Task<bool> IsPublicAccessEnabledAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if public rosters are restricted for a job (Jobs.bRestrictPublicRosters).
    /// When true, public roster listing/lookup is hidden.
    /// </summary>
    Task<bool> IsPublicRostersRestrictedAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get CC processing fee percent for a job.
    /// </summary>
    Task<decimal?> GetProcessingFeePercentAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get eCheck processing fee percent for a job.
    /// </summary>
    Task<decimal?> GetEcprocessingFeePercentAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get adult/team confirmation template for on-screen display.
    /// </summary>
    Task<AdultConfirmationInfo?> GetAdultConfirmationInfoAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get adult/team confirmation template for email.
    /// </summary>
    Task<AdultConfirmationEmailInfo?> GetAdultConfirmationEmailInfoAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get other jobs owned by the same customer as the specified job.
    /// Excludes the current job. Used for "Change Job" dropdown.
    /// </summary>
    Task<List<Dtos.RegistrationSearch.JobOptionDto>> GetOtherJobsForCustomerAsync(
        Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get upcoming jobs owned by the same customer (excludes the current job and expired jobs)
    /// that currently accept the given registration type. Populates the invite-link target-event
    /// dropdown so an invite can only point at an event actually open to that role. Filters on the
    /// accept-registration flag only (never the requires-token flag — inviting into open enrollment
    /// is valid).
    /// </summary>
    Task<List<Dtos.RegistrationSearch.JobOptionDto>> GetInviteTargetJobsForCustomerAsync(
        Guid jobId, Dtos.RegistrationSearch.InviteRegistrationKind kind, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all job IDs belonging to the same customer as the specified job.
    /// Used for Director field scoping — Directors see fields historically used by any of their customer's jobs.
    /// </summary>
    Task<List<Guid>> GetCustomerJobIdsAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get real-time availability pulse for a job (player/team reg, store, schedule, coming-soon).
    /// Public endpoint — no authentication required.
    /// </summary>
    Task<Dtos.JobPulseDto?> GetJobPulseAsync(string jobPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find currently-open Jobs for the role-selection "Looking for a new event?"
    /// panel: Jobs whose Customer is in the user's prior-history set, that the
    /// user is NOT already registered in, and that are publicly visible with
    /// the appropriate registration channel currently allowed —
    /// <c>BRegistrationAllowPlayer</c> for Family, <c>BRegistrationAllowTeam</c>
    /// for ClubRep.
    /// </summary>
    /// <param name="customerIds">Customers from the user's prior history.</param>
    /// <param name="excludeJobIds">Jobs the user already has an active registration in.</param>
    /// <param name="audience">Which open-window flag to gate on.</param>
    Task<List<Dtos.SuggestedEventDto>> GetCandidateEventsByCustomersAsync(
        IReadOnlyCollection<Guid> customerIds,
        IReadOnlyCollection<Guid> excludeJobIds,
        Dtos.SuggestedEventAudience audience,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Per-user, per-job overlay for the pulse: assigned team, owed total, regsaver
    /// purchased; ClubRep aggregates across owned teams. Caller must have already
    /// verified the regId belongs to the job in question (e.g. JWT jobPath matches).
    /// </summary>
    Task<Dtos.JobPulseUserContext> GetPulseUserContextAsync(Guid regId, string role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find the most recent prior-year sibling job (same CustomerId, JobTypeId, SportId, Season).
    /// Returns null if no prior-year sibling exists.
    /// </summary>
    Task<PriorYearJobInfo?> GetPriorYearJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check whether store walk-up registration is allowed for a given job.
    /// </summary>
    Task<bool> IsStoreWalkupAllowedAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the raw FACTS the registration-capability authority composes into create
    /// permissions: the create toggles, the data preconditions (fees configured / teams
    /// exist), the eventConcluded date inputs (schedule published + last game date +
    /// EventEndDate + ExpiryUsers), and the superseded-by-later-sibling flag. Pure read,
    /// no composition — the authority (<c>IJobRegistrationCapabilities</c>) folds these into
    /// door/toggle/precondition. Returns null when jobId is unknown (authority fails closed).
    /// </summary>
    Task<JobCapabilityFacts?> GetCapabilityFactsAsync(Guid jobId, CancellationToken cancellationToken = default);

    // ── Event Browse (public-facing mobile endpoints) ──

    Task<List<Dtos.EventListingDto>> GetActivePublicEventsAsync(CancellationToken ct = default);
    Task<Dtos.GameClockConfigDto?> GetGameClockConfigAsync(Guid jobId, CancellationToken ct = default);
    Task<Dtos.GameClockAvailableGameTimesDto> GetActiveGamesAsync(Guid jobId, DateTime? preferredGameDate, CancellationToken ct = default);
    Task<List<Dtos.EventDocDto>> GetJobDocsAsync(Guid jobId, CancellationToken ct = default);
}

public record JobAuthInfo
{
    public required Guid JobId { get; init; }
    public required string JobPath { get; init; }
    public string? LogoHeader { get; init; }
}

public record JobFeeSettings
{
    public bool? BTeamsFullPaymentRequired { get; init; }
    public bool? BAddProcessingFees { get; init; }
    public bool? BApplyProcessingFeesToTeamDeposit { get; init; }
    public required int PaymentMethodsAllowedCode { get; init; }
    public string? PlayerRegRefundPolicy { get; init; }
    public string? Season { get; init; }
    public string? PayTo { get; init; }
    public string? MailTo { get; init; }
    public string? MailinPaymentWarning { get; init; }
    public bool BEnableEcheck { get; init; }
    public bool BIncludeTeamDonation { get; init; }
    public bool? AdnArbTrial { get; init; }
    public DateTime? AdnArbStartDate { get; init; }
    public DateTime? AdnStartDateAfterTrial { get; init; }
}

/// <summary>
/// The raw, uncomposed facts about a job that the registration-capability authority needs.
/// One flat shape, one query — no derivation here. The authority applies
/// <c>JobLifecycle.EventConcluded</c> over the date inputs and the
/// <c>door AND toggle AND precondition</c> composition over the rest.
/// </summary>
public record JobCapabilityFacts
{
    // ── eventConcluded date inputs (the MUTATE door) ──
    /// <summary><c>BScheduleAllowPublicAccess</c> — published schedule unlocks the lastGameDate signal.</summary>
    public required bool SchedulePublished { get; init; }
    /// <summary>Latest scheduled game date, or null when no games are scheduled.</summary>
    public DateTime? LastGameDate { get; init; }
    /// <summary>Director-stated event end (<c>Jobs.EventEndDate</c>), or null — the signal bare-expiry missed.</summary>
    public DateTime? EventEndDate { get; init; }
    /// <summary><c>Jobs.ExpiryUsers</c> — last-resort eventConcluded fallback (non-null column).</summary>
    public required DateTime ExpiryUsers { get; init; }
    /// <summary>A live later-year sibling exists (same supersession heuristic the pulse uses).</summary>
    public required bool SupersededByLaterEvent { get; init; }

    // ── create toggles (director flags — admin is exempt from these) ──
    public required bool AllowPlayer { get; init; }   // BRegistrationAllowPlayer
    public required bool AllowTeam { get; init; }     // BRegistrationAllowTeam
    public required bool AllowStaff { get; init; }    // BRegistrationAllowStaff
    public required bool AllowReferee { get; init; }  // BRegistrationAllowReferee
    public required bool AllowRecruiter { get; init; } // BRegistrationAllowRecruiter
    public required bool ClubRepAllowAdd { get; init; }    // BClubRepAllowAdd
    public required bool ClubRepAllowEdit { get; init; }   // BClubRepAllowEdit
    public required bool ClubRepAllowDelete { get; init; } // BClubRepAllowDelete

    // ── data preconditions (facts — bind even admins) ──
    /// <summary>A Player-role JobFees row exists so a player reg can be priced.</summary>
    public required bool PlayerFeesConfigured { get; init; }
    /// <summary>A ClubRep-role JobFees row exists so a team reg can be priced.</summary>
    public required bool ClubRepFeesConfigured { get; init; }
    /// <summary>At least one team exists (a coach can only request a team once teams are in).</summary>
    public required bool TeamsExist { get; init; }
}

public record InsuranceOfferInfo
{
    public string? JobName { get; init; }
    public required bool BOfferPlayerRegsaverInsurance { get; init; }
    public required bool BOfferTeamRegsaverInsurance { get; init; }
    public DateTime? EventStartDate { get; init; }
    public DateTime? EventEndDate { get; init; }
}

public record JobConfirmationInfo
{
    public required Guid JobId { get; init; }
    public string? JobName { get; init; }
    public required string JobPath { get; init; }
    public bool? AdnArb { get; init; }
    public string? PlayerRegConfirmationOnScreen { get; init; }
}

public record JobConfirmationEmailInfo
{
    public required Guid JobId { get; init; }
    public string? JobName { get; init; }
    /// <summary>Public-facing job/org label (Jobs.DisplayName). Falls back to JobName when unset.</summary>
    public string? DisplayName { get; init; }
    public required string JobPath { get; init; }
    public bool? AdnArb { get; init; }
    public string? PlayerRegConfirmationEmail { get; init; }
    /// <summary>
    /// Date the USA Lacrosse membership must be valid through for this job, if any.
    /// Used by the USLax reconciliation tool to decide whether a given row needs action.
    /// </summary>
    public DateTime? UsLaxNumberValidThroughDate { get; init; }
}

public record AdultConfirmationInfo
{
    public required Guid JobId { get; init; }
    public string? JobName { get; init; }
    public required string JobPath { get; init; }
    public string? AdultRegConfirmationOnScreen { get; init; }
    public string? RegFormFrom { get; init; }
    public string? RegFormCcs { get; init; }
    public string? RegFormBccs { get; init; }
}

public record AdultConfirmationEmailInfo
{
    public required Guid JobId { get; init; }
    public string? JobName { get; init; }
    public required string JobPath { get; init; }
    public string? AdultRegConfirmationEmail { get; init; }
    public string? RegFormFrom { get; init; }
    public string? RegFormCcs { get; init; }
    public string? RegFormBccs { get; init; }
}

public record JobSeasonYear
{
    public string? Season { get; init; }
    public string? Year { get; init; }
}
