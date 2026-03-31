namespace TSIC.Contracts.Dtos;

public record FileUploadResponseDto
{
    public required string FileUrl { get; init; }
}
