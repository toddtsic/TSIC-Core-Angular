namespace TSIC.Contracts.Dtos.Email;

/// <summary>
/// Response from AI-format endpoints. Returns reformatted HTML only —
/// unlike AiComposeResponse which returns {subject, body}.
/// </summary>
public sealed record AiFormatResponse
{
    public required string Html { get; init; }
}
