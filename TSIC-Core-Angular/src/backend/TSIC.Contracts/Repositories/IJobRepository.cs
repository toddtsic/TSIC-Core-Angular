using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public record JobPreSubmitMetadata(
    string? PlayerProfileMetadataJson,
    string? JsonOptions,
    string? CoreRegformPlayer);

public record JobPaymentInfo(
    bool? AdnArb,
    int? AdnArbbillingOccurences,
    int? AdnArbintervalLength,
    DateTime? AdnArbstartDate);

public record JobMetadata(
    string? PlayerProfileMetadataJson,
    string? JsonOptions,
    string? CoreRegformPlayer);

public record JobMetadataDto(
    Guid JobId,
    string JobName,
    string JobPath,
    string? JobLogoPath,
    string? JobBannerPath,
    string? JobBannerText1,
    string? JobBannerText2,
    string? JobBannerBackgroundPath,
    bool? CoreRegformPlayer,
    DateTime? USLaxNumberValidThroughDate,
    DateTime? ExpiryUsers,
    string? PlayerProfileMetadataJson,
    string? JsonOptions,
    string? MomLabel,
    string? DadLabel,
    string? PlayerRegReleaseOfLiability,
    string? PlayerRegCodeOfConduct,
    string? PlayerRegCovid19Waiver,
    string? PlayerRegRefundPolicy,
    bool OfferPlayerRegsaverInsurance,
    bool? AdnArb,
    int? AdnArbBillingOccurences,
    int? AdnArbIntervalLength,
    DateTime? AdnArbStartDate,
    bool BRegistrationAllowTeam);

public record JobRegistrationStatus(
    bool BRegistrationAllowPlayer,
    DateTime? ExpiryUsers);

/// <summary>
/// Repository for managing Jobs entity data access.
/// </summary>
public interface IJobRepository
{
    /// <summary>
    /// Get a queryable for Job queries
    /// </summary>
    IQueryable<Jobs> Query();

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
}

public record InsuranceOfferInfo(
    string? JobName,
    bool BOfferPlayerRegsaverInsurance);

public record JobConfirmationInfo(
    Guid JobId,
    string? JobName,
    string JobPath,
    bool? AdnArb,
    string? PlayerRegConfirmationOnScreen);

public record JobConfirmationEmailInfo(
    Guid JobId,
    string? JobName,
    string JobPath,
    bool? AdnArb,
    string? PlayerRegConfirmationEmail);
