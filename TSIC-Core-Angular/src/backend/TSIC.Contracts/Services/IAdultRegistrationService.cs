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

    /// <summary>
    /// Validates form fields and resolves fees before the payment step.
    /// In login-mode (userId provided), creates the registration and stamps fees.
    /// In create-mode (userId null), returns fee preview only.
    /// </summary>
    Task<PreSubmitAdultRegResponseDto> PreSubmitAsync(Guid jobId, string? userId, PreSubmitAdultRegRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes payment for an existing adult registration (login-mode).
    /// </summary>
    Task<AdultPaymentResponseDto> ProcessPaymentAsync(Guid registrationId, string userId, AdultPaymentRequestDto request, CancellationToken cancellationToken = default);
}
