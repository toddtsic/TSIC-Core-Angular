namespace TSIC.Contracts.Dtos.NuveiUpload;

public record NuveiUploadResultDto
{
    public required int TotalRows { get; init; }
    public required int ImportedCount { get; init; }
    public required int DuplicateCount { get; init; }
    public required int ErrorCount { get; init; }
    public required List<NuveiUploadRowError> Errors { get; init; }
}

public record NuveiUploadRowError
{
    public required int Row { get; init; }
    public required string Reason { get; init; }
}
