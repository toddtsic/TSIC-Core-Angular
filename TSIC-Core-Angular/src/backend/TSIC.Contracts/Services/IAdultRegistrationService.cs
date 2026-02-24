using TSIC.Contracts.Dtos.AdultRegistration;

namespace TSIC.Contracts.Services;

public interface IAdultRegistrationService
{
    Task<AdultRegJobInfoResponse> GetJobInfoByPathAsync(string jobPath, CancellationToken cancellationToken = default);
    Task<AdultRegFormResponse> GetFormSchemaForRoleAsync(string jobPath, AdultRoleType roleType, CancellationToken cancellationToken = default);
    Task<AdultRegistrationResponse> RegisterNewUserAsync(string jobPath, AdultRegistrationRequest request, CancellationToken cancellationToken = default);
    Task<AdultRegistrationResponse> RegisterExistingUserAsync(Guid jobId, string userId, AdultRegistrationExistingRequest request, string auditUserId, CancellationToken cancellationToken = default);
    Task<AdultConfirmationResponse> GetConfirmationAsync(Guid registrationId, CancellationToken cancellationToken = default);
    Task SendConfirmationEmailAsync(Guid registrationId, CancellationToken cancellationToken = default);
}
