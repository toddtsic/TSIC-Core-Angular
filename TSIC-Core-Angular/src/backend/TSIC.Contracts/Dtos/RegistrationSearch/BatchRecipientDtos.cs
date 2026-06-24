namespace TSIC.Contracts.Dtos.RegistrationSearch;

/// <summary>
/// One registrant's own contact email, keyed by registration. Bulk-loaded once for a batch send
/// so the render loop resolves recipients from memory instead of a query per recipient.
/// </summary>
public record BatchRegistrantEmailDto
{
    public required Guid RegistrationId { get; init; }
    public string? Email { get; init; }
}

/// <summary>
/// A family's parent contact emails, keyed by FamilyUserId. Bulk-loaded once for a batch send so a
/// player recipient's mom/dad addresses are an in-memory lookup, not a per-recipient Families query.
/// </summary>
public record BatchFamilyEmailsDto
{
    public required string FamilyUserId { get; init; }
    public string? MomEmail { get; init; }
    public string? DadEmail { get; init; }
}
