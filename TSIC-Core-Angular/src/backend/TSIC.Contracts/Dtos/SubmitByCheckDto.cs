namespace TSIC.Contracts.Dtos;

public record SubmitByCheckRequestDto
{
    public required string JobPath { get; init; } = string.Empty;
    public required List<Guid> RegistrationIds { get; init; } = new();
}

public record SubmitByCheckResponseDto
{
    public required bool Success { get; init; }
    public required string Message { get; init; } = string.Empty;
    public required List<Guid> UpdatedRegistrationIds { get; init; } = new();
    public required List<SubmitByCheckRejectionDto> Rejections { get; init; } = new();
}

public record SubmitByCheckRejectionDto
{
    public required Guid RegistrationId { get; init; }
    public required string Reason { get; init; } = string.Empty;
}
