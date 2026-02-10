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

    // Accounting records
    public required List<AccountingRecordDto> AccountingRecords { get; init; }
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
