namespace TSIC.Contracts.Services;

public interface IFileUploadService
{
    /// <summary>
    /// Upload a file and return its public URL.
    /// </summary>
    Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType, CancellationToken ct = default);

    /// <summary>
    /// Delete a file by its URL or path.
    /// </summary>
    Task<bool> DeleteFileAsync(string fileUrl, CancellationToken ct = default);
}
