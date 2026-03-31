using TSIC.Contracts.Services;

namespace TSIC.API.Services.Shared.Files;

/// <summary>
/// File upload service — stores files to the configured file storage path.
/// </summary>
public sealed class FileUploadService : IFileUploadService
{
    private readonly string _uploadPath;
    private readonly string _staticsBaseUrl;
    private readonly ILogger<FileUploadService> _logger;

    public FileUploadService(IConfiguration configuration, ILogger<FileUploadService> logger)
    {
        _uploadPath = configuration["FileStorage:UploadFilesPath"]
            ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
        _staticsBaseUrl = configuration["TsicSettings:StaticsBaseUrl"]
            ?? "https://statics.teamsportsinfo.com";
        _logger = logger;

        if (!Directory.Exists(_uploadPath))
            Directory.CreateDirectory(_uploadPath);
    }

    public async Task<string> UploadFileAsync(
        Stream fileStream, string fileName, string contentType, CancellationToken ct = default)
    {
        var safeFileName = $"{Guid.NewGuid():N}_{Path.GetFileName(fileName)}";
        var filePath = Path.Combine(_uploadPath, safeFileName);

        await using var outputStream = File.Create(filePath);
        await fileStream.CopyToAsync(outputStream, ct);

        _logger.LogInformation("File uploaded: {FileName} -> {FilePath}", fileName, filePath);

        return $"{_staticsBaseUrl.TrimEnd('/')}/uploads/{safeFileName}";
    }

    public Task<bool> DeleteFileAsync(string fileUrl, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(new Uri(fileUrl).AbsolutePath);
        var filePath = Path.Combine(_uploadPath, fileName);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogInformation("File deleted: {FilePath}", filePath);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }
}
