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

public record ArbEmailResultDto
{
    public required int EmailsSent { get; init; }
    public required int EmailsFailed { get; init; }
    public List<string> FailedAddresses { get; init; } = [];
}

public record ArbSubstitutionVariableDto
{
    public required string Token { get; init; }
    public required string Label { get; init; }
}
