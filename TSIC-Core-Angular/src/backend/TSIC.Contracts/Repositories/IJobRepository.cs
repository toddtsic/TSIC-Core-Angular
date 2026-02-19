using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public record JobPreSubmitMetadata
{
    public string? PlayerProfileMetadataJson { get; init; }
    public string? JsonOptions { get; init; }
    public string? CoreRegformPlayer { get; init; }
}

public record JobPaymentInfo
{
    public bool? AdnArb { get; init; }
    public int? AdnArbbillingOccurences { get; init; }
    public int? AdnArbintervalLength { get; init; }
    public DateTime? AdnArbstartDate { get; init; }
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
    public required bool BRegistrationAllowTeam { get; init; }
    public required bool BBannerIsCustom { get; init; }
    public string? JobTypeName { get; init; }
}

public record JobRegistrationStatus
{
    public required bool BRegistrationAllowPlayer { get; init; }
    public DateTime? ExpiryUsers { get; init; }
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
    /// Get processing fee percent for a job.
    /// </summary>
    Task<decimal?> GetProcessingFeePercentAsync(Guid jobId, CancellationToken cancellationToken = default);

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
    /// Get all job IDs belonging to the same customer as the specified job.
    /// Used for Director field scoping — Directors see fields historically used by any of their customer's jobs.
    /// </summary>
    Task<List<Guid>> GetCustomerJobIdsAsync(Guid jobId, CancellationToken cancellationToken = default);

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
