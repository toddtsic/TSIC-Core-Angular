using System.Text.Json;
using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Dtos.AdultRegistration;

/// <summary>
/// Adult role types available for registration.
/// </summary>
public enum AdultRoleType
{
    UnassignedAdult = 0,
    Referee = 1,
    Recruiter = 2
}

/// <summary>
/// Job-level info returned when the wizard first loads.
/// </summary>
public record AdultRegJobInfoResponse
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
    public required List<AdultRoleOption> AvailableRoles { get; init; }
}

/// <summary>
/// A role choice displayed in the wizard's role-selection step.
/// </summary>
public record AdultRoleOption
{
    public required AdultRoleType RoleType { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
}

/// <summary>
/// Dynamic form schema + waivers for a selected role.
/// </summary>
public record AdultRegFormResponse
{
    public required AdultRoleType RoleType { get; init; }
    public required List<JobRegFieldDto> Fields { get; init; }
    public required List<AdultWaiverDto> Waivers { get; init; }
}

/// <summary>
/// A single waiver that must be accepted.
/// </summary>
public record AdultWaiverDto
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string HtmlContent { get; init; }
}

/// <summary>
/// Request to register a new adult (creates new user + registration).
/// Payment fields are optional — included when job has fees for the role.
/// </summary>
public record AdultRegistrationRequest
{
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public required string Phone { get; init; }
    public required AdultRoleType RoleType { get; init; }
    public Dictionary<string, JsonElement>? FormValues { get; init; }
    public Dictionary<string, bool>? WaiverAcceptance { get; init; }
    public CreditCardInfo? CreditCard { get; init; }
    public string? PaymentMethod { get; init; }
}

/// <summary>
/// Request to register an existing (logged-in) user as an adult.
/// </summary>
public record AdultRegistrationExistingRequest
{
    public required AdultRoleType RoleType { get; init; }
    public Dictionary<string, JsonElement>? FormValues { get; init; }
    public Dictionary<string, bool>? WaiverAcceptance { get; init; }
}

/// <summary>
/// Response after successful adult registration.
/// </summary>
public record AdultRegistrationResponse
{
    public required bool Success { get; init; }
    public required Guid RegistrationId { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Confirmation content returned after registration.
/// </summary>
public record AdultConfirmationResponse
{
    public required Guid RegistrationId { get; init; }
    public required string ConfirmationHtml { get; init; }
    public required string RoleDisplayName { get; init; }
}

/// <summary>
/// Internal projection for job data needed during adult registration.
/// </summary>
public record AdultRegJobData
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
    public required int JobAi { get; init; }
    public required bool BAddProcessingFees { get; init; }
    public string? AdultProfileMetadataJson { get; init; }
    public string? JsonOptions { get; init; }
    public string? AdultRegConfirmationEmail { get; init; }
    public string? AdultRegConfirmationOnScreen { get; init; }
    public string? AdultRegRefundPolicy { get; init; }
    public string? AdultRegReleaseOfLiability { get; init; }
    public string? AdultRegCodeOfConduct { get; init; }
    public string? RefereeRegConfirmationEmail { get; init; }
    public string? RefereeRegConfirmationOnScreen { get; init; }
    public string? RecruiterRegConfirmationEmail { get; init; }
    public string? RecruiterRegConfirmationOnScreen { get; init; }
}

// ── PreSubmit DTOs ────────────────────────────────────────────────

/// <summary>
/// PreSubmit request — validates form fields and resolves fees before payment.
/// </summary>
public record PreSubmitAdultRegRequestDto
{
    public required AdultRoleType RoleType { get; init; }
    public Dictionary<string, JsonElement>? FormValues { get; init; }
    public Dictionary<string, bool>? WaiverAcceptance { get; init; }
}

/// <summary>
/// PreSubmit response — validation result + fee breakdown.
/// RegistrationId is null in create-mode (user doesn't exist yet).
/// </summary>
public record PreSubmitAdultRegResponseDto
{
    public required bool Valid { get; init; }
    public List<AdultValidationErrorDto>? ValidationErrors { get; init; }
    public Guid? RegistrationId { get; init; }
    public required AdultFeeBreakdownDto Fees { get; init; }
}

/// <summary>
/// Fee breakdown returned by preSubmit for the payment step.
/// </summary>
public record AdultFeeBreakdownDto
{
    public required decimal FeeBase { get; init; }
    public required decimal FeeProcessing { get; init; }
    public required decimal FeeDiscount { get; init; }
    public required decimal FeeLateFee { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal OwedTotal { get; init; }
}

/// <summary>
/// A field-level validation error from preSubmit.
/// </summary>
public record AdultValidationErrorDto
{
    public required string Field { get; init; }
    public required string Message { get; init; }
}

// ── Payment DTOs ──────────────────────────────────────────────────

/// <summary>
/// Payment request for an existing adult registration (login-mode).
/// </summary>
public record AdultPaymentRequestDto
{
    public required Guid RegistrationId { get; init; }
    public CreditCardInfo? CreditCard { get; init; }
    public required string PaymentMethod { get; init; }
}

/// <summary>
/// Payment response after processing adult registration payment.
/// </summary>
public record AdultPaymentResponseDto
{
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public string? TransactionId { get; init; }
    public string? ErrorCode { get; init; }
}
