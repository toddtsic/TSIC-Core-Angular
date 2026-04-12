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
    /// Get teams available for Coach role selection (excludes Waitlist/Dropped).
    /// </summary>
    Task<List<AdultTeamOptionDto>> GetAvailableTeamsAsync(string jobPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unified role configuration for the wizard. Validates role key + job type,
    /// enforces the minor-PII security invariant (tournament coach + BAllowRosterViewAdult=false),
    /// and returns the profile fields / waivers / display metadata for the given role.
    /// Throws <see cref="InvalidOperationException"/> when the invariant is violated
    /// or the role+job-type combination is not supported.
    /// </summary>
    Task<AdultRoleConfigDto> GetRoleConfigAsync(string jobPath, string roleKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the authenticated user's existing active registrations for the
    /// (jobPath, roleKey) pair — used to prefill the Profile step when a returning
    /// user re-enters the wizard. Staff returns N team IDs (one per coaching team);
    /// other roles return zero or one row with no teams.
    /// </summary>
    Task<AdultExistingRegistrationDto> GetMyExistingRegistrationAsync(string jobPath, string roleKey, string userId, CancellationToken cancellationToken = default);

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
