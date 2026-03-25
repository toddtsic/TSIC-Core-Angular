namespace TSIC.Contracts.Dtos.Email;

public record AiComposeRequest
{
    public required string Prompt { get; init; }
}

public record AiComposeResponse
{
    public required string Subject { get; init; }
    public required string Body { get; init; }
}
