namespace TSIC.Contracts.Dtos.RegSaverUpload;

public record RegSaverUploadResultDto
{
    public required int TotalRows { get; init; }
    public required int ImportedCount { get; init; }
    public required int DuplicateCount { get; init; }
    public required int ErrorCount { get; init; }
    public required List<RegSaverUploadRowError> Errors { get; init; }
}

public record RegSaverUploadRowError
{
    public required int Row { get; init; }
    public required string PolicyNumber { get; init; }
    public required string Reason { get; init; }
}
