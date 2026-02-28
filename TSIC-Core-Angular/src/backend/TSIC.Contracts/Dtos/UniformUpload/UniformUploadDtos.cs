namespace TSIC.Contracts.Dtos.UniformUpload;

/// <summary>
/// Result of a uniform number upload operation.
/// </summary>
public record UniformUploadResultDto
{
    public required int TotalRows { get; init; }
    public required int UpdatedCount { get; init; }
    public required int SkippedCount { get; init; }
    public required int ErrorCount { get; init; }
    public required List<UniformUploadRowError> Errors { get; init; }
}

/// <summary>
/// Per-row error detail from uniform number upload.
/// </summary>
public record UniformUploadRowError
{
    public required int Row { get; init; }
    public required string RegistrationId { get; init; }
    public required string Reason { get; init; }
}
