using System.Text.Json;
using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Dtos.AdultRegistration;

/// <summary>
/// Adult role types — legacy numeric enum.
/// </summary>
/// <remarks>
/// DEPRECATED for role assignment. The authoritative role is resolved server-side from
/// <c>(RoleKey, Jobs.JobTypeId)</c> per the security model in <c>IAdultRegistrationService</c>.
/// Kept for backward compatibility with existing confirmation templates and admin queries.
/// </remarks>
public enum AdultRoleType
{
    UnassignedAdult = 0,
    Referee = 1,
    Recruiter = 2,
    Staff = 3
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
/// All identity/contact/address fields match legacy StaffRegister/RefRegister view models.
/// Payment fields are optional — included when job has fees for the role.
/// </summary>
public record AdultRegistrationRequest
{
    // Credentials
    public required string Username { get; init; }
    public required string Password { get; init; }

    // Identity
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Gender { get; init; }

    // Contact
    public required string Email { get; init; }
    public required string Phone { get; init; }
    public string? CellphoneProvider { get; init; }

    // Address
    public required string StreetAddress { get; init; }
    public required string City { get; init; }
    public required string State { get; init; }
    public required string PostalCode { get; init; }

    // Role intent from URL — authoritative. Backend resolves this + JobTypeId → actual RoleId.
    // Allowed values: "coach" | "referee" | "recruiter" (see AdultRegRoleKeys).
    public required string RoleKey { get; init; }

    // Deprecated: kept for backward compat only. Role is NOT driven by this field.
    public AdultRoleType? RoleType { get; init; }

    public required bool AcceptedTos { get; init; }
    public Dictionary<string, JsonElement>? FormValues { get; init; }
    public Dictionary<string, bool>? WaiverAcceptance { get; init; }

    // Coach-only: teams they want to coach (first is primary → AssignedTeamId)
    public List<Guid>? TeamIdsCoaching { get; init; }

    // Payment (optional — only if job has fees)
    public CreditCardInfo? CreditCard { get; init; }
    public string? PaymentMethod { get; init; }
}

/// <summary>
/// Available team option for Coach registration (legacy ListAvailableTeams equivalent).
/// </summary>
public record AdultTeamOptionDto
{
    public required Guid TeamId { get; init; }
    public required string ClubName { get; init; }
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required string TeamName { get; init; }
    /// <summary>Full display text: "Club:Agegroup:Div:TeamName" (matches legacy format).</summary>
    public required string DisplayText { get; init; }
}

/// <summary>
/// Request to register an existing (logged-in) user as an adult.
/// </summary>
public record AdultRegistrationExistingRequest
{
    public required string RoleKey { get; init; }
    public AdultRoleType? RoleType { get; init; } // deprecated — see AdultRegistrationRequest
    public Dictionary<string, JsonElement>? FormValues { get; init; }
    public Dictionary<string, bool>? WaiverAcceptance { get; init; }
    public List<Guid>? TeamIdsCoaching { get; init; }
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
    public required int JobTypeId { get; init; }
    public required bool BAllowRosterViewAdult { get; init; }
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
    public required string RoleKey { get; init; }
    public AdultRoleType? RoleType { get; init; } // deprecated
    public Dictionary<string, JsonElement>? FormValues { get; init; }
    public Dictionary<string, bool>? WaiverAcceptance { get; init; }
    public List<Guid>? TeamIdsCoaching { get; init; }
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

// ── Existing registration (for returning users — prefill + edit) ──

/// <summary>
/// Returned by <c>GET /adult-registration/{jobPath}/my-registration/{roleKey}</c>.
/// Surfaces a returning authenticated user's existing active registrations so the
/// wizard can prefill the Profile step (team selections, form values, waivers).
/// </summary>
public record AdultExistingRegistrationDto
{
    /// <summary>True when one or more active registrations exist for (user, job, role).</summary>
    public required bool HasExisting { get; init; }

    /// <summary>All registration IDs in the set — used later for delete-all + recreate update.</summary>
    public required List<Guid> RegistrationIds { get; init; }

    /// <summary>Assigned team IDs across all rows (empty for non-team roles).</summary>
    public required List<Guid> TeamIds { get; init; }

    /// <summary>Dynamic form values (from first row; adult registrations share them).</summary>
    public Dictionary<string, JsonElement>? FormValues { get; init; }

    /// <summary>Waiver acceptance state (from first row).</summary>
    public Dictionary<string, bool>? WaiverAcceptance { get; init; }
}

// ── Role config (new unified contract) ────────────────────────────

/// <summary>
/// Complete role configuration for the adult wizard — everything the frontend
/// needs to render the Profile/Waivers steps for a given (job, roleKey) pair.
/// Returned by <c>GET /adult-registration/{jobPath}/role-config/{roleKey}</c>.
///
/// The <see cref="NeedsTeamSelection"/> flag is derived from job type:
/// only true for coach in Tournament context. Club/League coaches register
/// as UnassignedAdult and don't self-roster.
/// </summary>
public record AdultRoleConfigDto
{
    /// <summary>URL role key — "coach" | "referee" | "recruiter".</summary>
    public required string RoleKey { get; init; }

    /// <summary>User-facing title: "Coach / Volunteer", "Referee", "College Recruiter".</summary>
    public required string DisplayName { get; init; }

    /// <summary>Short description for step headers or confirmation.</summary>
    public required string Description { get; init; }

    /// <summary>Bootstrap icon class — e.g. "bi-person-badge".</summary>
    public required string Icon { get; init; }

    /// <summary>
    /// True when the Profile step must present the team multi-select.
    /// Only true for coach + Tournament job. Club/League coaches get UA status,
    /// no self-rostering.
    /// </summary>
    public required bool NeedsTeamSelection { get; init; }

    /// <summary>Dynamic form fields for the Profile step (from job metadata + fallback).</summary>
    public required List<JobRegFieldDto> ProfileFields { get; init; }

    /// <summary>Job-configured waivers that must be accepted.</summary>
    public required List<AdultWaiverDto> Waivers { get; init; }
}
