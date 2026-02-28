using TSIC.Contracts.Dtos.UniformUpload;

namespace TSIC.Contracts.Services;

public interface IUniformUploadService
{
    /// <summary>
    /// Generate an Excel template pre-populated with the job's player roster.
    /// Columns: RegistrationId, FirstName, LastName, TeamName, UniformNo, DayGroup.
    /// </summary>
    Task<byte[]> GenerateTemplateAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Parse an uploaded Excel file and bulk-update UniformNo/DayGroup on matching registrations.
    /// Partial success: valid rows are applied, invalid rows are reported individually.
    /// </summary>
    Task<UniformUploadResultDto> ProcessUploadAsync(Guid jobId, Stream fileStream, CancellationToken ct = default);
}
