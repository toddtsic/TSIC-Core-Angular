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
    /// Check if job uses waitlists.
    /// </summary>
    Task<bool> GetUsesWaitlistsAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if public schedule/roster access is enabled for a job.
    /// </summary>
    Task<bool> IsPublicAccessEnabledAsync(Guid jobId, CancellationToken cancellationToken = default);

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
    /// Get other jobs owned by the same customer, excluding past jobs (expired).
    /// Used for club rep invite link target job dropdown.
    /// </summary>
    Task<List<Dtos.RegistrationSearch.JobOptionDto>> GetFutureJobsForCustomerAsync(
        Guid jobId, CancellationToken cancellationToken = default);

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
    /// Job-level team-registration capability flags consumed by ClubRep-facing endpoints
    /// to gate add/edit/delete operations. Mirrors the three <c>BClubRepAllow*</c> columns
    /// plus the global <c>BRegistrationAllowTeam</c> gate. Returns null when jobId is unknown.
    /// </summary>
    Task<JobTeamCapabilities?> GetTeamCapabilitiesAsync(Guid jobId, CancellationToken cancellationToken = default);

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
}

public record JobTeamCapabilities
{
    public required bool TeamRegistrationOpen { get; init; }
    public required bool ClubRepAllowAdd { get; init; }
    public required bool ClubRepAllowEdit { get; init; }
    public required bool ClubRepAllowDelete { get; init; }
}

public record InsuranceOfferInfo
{
    public string? JobName { get; init; }
    public required bool BOfferPlayerRegsaverInsurance { get; init; }
    public required bool BOfferTeamRegsaverInsurance { get; init; }
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
