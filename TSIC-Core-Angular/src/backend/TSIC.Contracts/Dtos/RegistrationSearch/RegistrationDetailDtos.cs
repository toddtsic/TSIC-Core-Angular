namespace TSIC.Contracts.Dtos.RegistrationSearch;

/// <summary>
/// Full registration detail for the slide-over panel.
/// Includes profile values, metadata schema, and accounting records.
/// </summary>
public record RegistrationDetailDto
{
    // Identity
    public required Guid RegistrationId { get; init; }
    public required int RegistrationAi { get; init; }

    // Person (from AspNetUsers)
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public string? Phone { get; init; }

    // Context
    public required string RoleName { get; init; }
    public required bool Active { get; init; }
    public string? TeamName { get; init; }

    // Financials (summary)
    public required decimal FeeBase { get; init; }
    public required decimal FeeProcessing { get; init; }
    public required decimal FeeDiscount { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }

    // Dynamic profile fields (key = metadata field name, value = current value as string)
    public required Dictionary<string, string?> ProfileValues { get; init; }

    // Metadata schema (from Job.PlayerProfileMetadataJson — for form rendering)
    public string? ProfileMetadataJson { get; init; }

    // Parent/guardian labels (from Jobs entity, defaults "Mom"/"Dad")
    public string MomLabel { get; init; } = "Mom";
    public string DadLabel { get; init; } = "Dad";

    // Family contact info (from Families entity, null if no family link)
    public FamilyContactDto? FamilyContact { get; init; }

    // User demographics (from AspNetUsers)
    public UserDemographicsDto? UserDemographics { get; init; }

    // Registration timestamps
    public DateTime? RegistrationDate { get; init; }
    public DateTime? ModifiedDate { get; init; }

    // ARB subscription (true when registration has an AdnSubscriptionId)
    public bool HasSubscription { get; init; }

    // Accounting records
    public required List<AccountingRecordDto> AccountingRecords { get; init; }
}

/// <summary>
/// Family contact information from the Families entity.
/// </summary>
public record FamilyContactDto
{
    public string? MomFirstName { get; init; }
    public string? MomLastName { get; init; }
    public string? MomCellphone { get; init; }
    public string? MomEmail { get; init; }
    public string? DadFirstName { get; init; }
    public string? DadLastName { get; init; }
    public string? DadCellphone { get; init; }
    public string? DadEmail { get; init; }
}

/// <summary>
/// User demographics from AspNetUsers.
/// </summary>
public record UserDemographicsDto
{
    public string? Email { get; init; }
    public string? Cellphone { get; init; }
    public string? Gender { get; init; }
    public DateTime? DateOfBirth { get; init; }
    public string? StreetAddress { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
}

/// <summary>
/// Update family contact info for a registration's linked family.
/// </summary>
public record UpdateFamilyContactRequest
{
    public required Guid RegistrationId { get; init; }
    public required FamilyContactDto FamilyContact { get; init; }
}

/// <summary>
/// Update user demographics for a registration's linked user.
/// </summary>
public record UpdateUserDemographicsRequest
{
    public required Guid RegistrationId { get; init; }
    public required UserDemographicsDto Demographics { get; init; }
}

/// <summary>
/// Update registration profile request.
/// Key = dbColumn name, Value = new value as string.
/// </summary>
public record UpdateRegistrationProfileRequest
{
    public required Guid RegistrationId { get; init; }
    public required Dictionary<string, string?> ProfileValues { get; init; }
}

/// <summary>
/// Batch email request — sends to multiple registrations with token substitution.
/// </summary>
public record BatchEmailRequest
{
    public required List<Guid> RegistrationIds { get; init; }
    public required string Subject { get; init; }
    public required string BodyTemplate { get; init; }
}

/// <summary>
/// Batch email result with sent/failed counts.
/// </summary>
public record BatchEmailResponse
{
    public required int TotalRecipients { get; init; }
    public required int Sent { get; init; }
    public required int Failed { get; init; }
    public required List<string> FailedAddresses { get; init; }
}

/// <summary>
/// Email preview request — renders tokens for N recipients without sending.
/// </summary>
public record EmailPreviewRequest
{
    public required List<Guid> RegistrationIds { get; init; }
    public required string Subject { get; init; }
    public required string BodyTemplate { get; init; }
}

/// <summary>
/// Email preview response with rendered previews.
/// </summary>
public record EmailPreviewResponse
{
    public required List<RenderedEmailPreview> Previews { get; init; }
}

public record RenderedEmailPreview
{
    public required string RecipientName { get; init; }
    public required string RecipientEmail { get; init; }
    public required string RenderedSubject { get; init; }
    public required string RenderedBody { get; init; }
}

/// <summary>
/// Request to change a registration's job.
/// </summary>
public record ChangeJobRequest
{
    public required Guid NewJobId { get; init; }
}

/// <summary>
/// Response from a change-job operation.
/// </summary>
public record ChangeJobResponse
{
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public string? NewJobName { get; init; }
}

/// <summary>
/// Lightweight job info for the "Change Job" dropdown.
/// </summary>
public record JobOptionDto
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
}

/// <summary>
/// Response from a delete-registration operation.
/// </summary>
public record DeleteRegistrationResponse
{
    public required bool Success { get; init; }
    public string? Message { get; init; }
}
