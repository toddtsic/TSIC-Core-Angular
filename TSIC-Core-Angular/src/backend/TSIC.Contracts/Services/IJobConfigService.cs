using TSIC.Contracts.Dtos.JobConfig;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for managing job configuration (AdminOnly — Directors + SuperUsers).
/// Role-based field filtering applied at this layer.
/// </summary>
public interface IJobConfigService
{
    // Single load — returns ALL categories, role-filtered
    Task<JobConfigFullDto> GetFullConfigAsync(Guid jobId, bool isSuperUser, CancellationToken ct = default);

    // Per-category writes
    Task UpdateGeneralAsync(Guid jobId, UpdateJobConfigGeneralRequest req, bool isSuperUser, CancellationToken ct = default);
    Task UpdatePaymentAsync(Guid jobId, UpdateJobConfigPaymentRequest req, bool isSuperUser, CancellationToken ct = default);
    Task UpdateCommunicationsAsync(Guid jobId, UpdateJobConfigCommunicationsRequest req, CancellationToken ct = default);
    Task UpdatePlayerAsync(Guid jobId, UpdateJobConfigPlayerRequest req, bool isSuperUser, CancellationToken ct = default);
    Task UpdateTeamsAsync(Guid jobId, UpdateJobConfigTeamsRequest req, bool isSuperUser, CancellationToken ct = default);
    Task UpdateCoachesAsync(Guid jobId, UpdateJobConfigCoachesRequest req, CancellationToken ct = default);
    Task UpdateSchedulingAsync(Guid jobId, UpdateJobConfigSchedulingRequest req, CancellationToken ct = default);
    Task UpdateMobileStoreAsync(Guid jobId, UpdateJobConfigMobileStoreRequest req, bool isSuperUser, CancellationToken ct = default);
    Task UpdateBrandingAsync(Guid jobId, UpdateJobConfigBrandingRequest req, CancellationToken ct = default);
    Task UpdateBrandingImageFieldAsync(Guid jobId, string conventionName, string? fileName, CancellationToken ct = default);

    // Reference data
    Task<JobConfigReferenceDataDto> GetReferenceDataAsync(CancellationToken ct = default);

    // Admin charges CRUD (SuperUser only — enforced at controller level)
    Task<JobAdminChargeDto> AddAdminChargeAsync(Guid jobId, CreateAdminChargeRequest req, CancellationToken ct = default);
    Task DeleteAdminChargeAsync(Guid jobId, int chargeId, CancellationToken ct = default);
}
