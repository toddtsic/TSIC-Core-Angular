namespace TSIC.Contracts.Dtos.Arb;

public record ArbSendEmailsRequest
{
    public required Guid JobId { get; init; }
    public required string SenderUserId { get; init; }
    public required ArbFlagType FlagType { get; init; }
    public required string EmailSubject { get; init; }
    public required string EmailBody { get; init; }
    public required List<Guid> RegistrationIds { get; init; }
    public bool NotifyDirectors { get; init; }
}

/// <summary>
/// Sandbox-only test send for the ARB defensive compose: renders the body's ARB tokens against
/// one flagged registrant and delivers the result to Superusers and/or one explicit inbox.
/// JobId is overridden from JWT claims server-side (same as ArbSendEmailsRequest).
/// </summary>
public record ArbTestSendRequest
{
    public required Guid JobId { get; init; }
    public required ArbFlagType FlagType { get; init; }
    /// <summary>The flagged registrant whose data renders the tokens (first selected row).</summary>
    public required Guid RegistrationId { get; init; }
    public required string EmailSubject { get; init; }
    public required string EmailBody { get; init; }
    public bool IncludeSuperusers { get; init; } = true;
    public string? ExtraRecipient { get; init; }
}

public record ArbSubstitutionVariableDto
{
    public required string Token { get; init; }
    public required string Label { get; init; }
}
