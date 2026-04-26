using Microsoft.Extensions.Options;
using TSIC.Contracts.Configuration;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Shared.Files;

public sealed class MedFormService : IMedFormService
{
    private const long MaxBytes = 10L * 1024 * 1024; // 10 MB
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private readonly string _path;
    private readonly ILogger<MedFormService> _logger;

    public MedFormService(IOptions<FileStorageOptions> options, ILogger<MedFormService> logger)
    {
        _path = options.Value.MedFormsPath
            ?? throw new InvalidOperationException("FileStorage:MedFormsPath is not configured.");
        _logger = logger;
        if (!Directory.Exists(_path))
            Directory.CreateDirectory(_path);
    }

    public bool Exists(string playerUserId)
    {
        if (!IsValidPlayerUserId(playerUserId)) return false;
        return File.Exists(GetFilePath(playerUserId));
    }

    public async Task<MedFormUploadResult> UploadAsync(
        string playerUserId, Stream content, long length, CancellationToken ct = default)
    {
        if (!IsValidPlayerUserId(playerUserId))
            return new MedFormUploadResult { Status = MedFormUploadStatus.InvalidPlayerUserId };

        if (length <= 0 || length > MaxBytes)
            return new MedFormUploadResult { Status = MedFormUploadStatus.TooLarge };

        // Magic-byte sniff (don't trust the client's Content-Type header).
        var prefix = new byte[PdfMagic.Length];
        var read = 0;
        while (read < PdfMagic.Length)
        {
            var n = await content.ReadAsync(prefix.AsMemory(read, PdfMagic.Length - read), ct);
            if (n == 0) break;
            read += n;
        }
        if (read < PdfMagic.Length || !prefix.AsSpan().SequenceEqual(PdfMagic))
            return new MedFormUploadResult { Status = MedFormUploadStatus.InvalidPdf };

        var finalPath = GetFilePath(playerUserId);
        var tempPath = finalPath + ".tmp." + Guid.NewGuid().ToString("N");

        try
        {
            await using (var output = File.Create(tempPath))
            {
                await output.WriteAsync(prefix, ct);
                await content.CopyToAsync(output, ct);
                if (output.Length > MaxBytes)
                {
                    output.Close();
                    File.Delete(tempPath);
                    return new MedFormUploadResult { Status = MedFormUploadStatus.TooLarge };
                }
            }

            // Atomic replace — overwrite any prior file.
            File.Move(tempPath, finalPath, overwrite: true);
            _logger.LogInformation(
                "MedForm uploaded for player {PlayerUserId} ({Bytes} bytes).",
                playerUserId, length);
            return new MedFormUploadResult { Status = MedFormUploadStatus.Ok };
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }

    public Task<Stream?> ReadAsync(string playerUserId, CancellationToken ct = default)
    {
        if (!IsValidPlayerUserId(playerUserId)) return Task.FromResult<Stream?>(null);
        var path = GetFilePath(playerUserId);
        if (!File.Exists(path)) return Task.FromResult<Stream?>(null);
        Stream s = File.OpenRead(path);
        return Task.FromResult<Stream?>(s);
    }

    public Task<bool> DeleteAsync(string playerUserId, CancellationToken ct = default)
    {
        if (!IsValidPlayerUserId(playerUserId)) return Task.FromResult(false);
        var path = GetFilePath(playerUserId);
        if (!File.Exists(path)) return Task.FromResult(false);
        File.Delete(path);
        _logger.LogInformation("MedForm deleted for player {PlayerUserId}.", playerUserId);
        return Task.FromResult(true);
    }

    private string GetFilePath(string playerUserId) =>
        Path.Combine(_path, $"{playerUserId}.pdf");

    // Defense in depth: never let a request escape the configured directory
    // via path-traversal segments in the userId. Identity userIds are GUIDs,
    // so we accept only standard-format characters.
    private static bool IsValidPlayerUserId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (id.Length is < 8 or > 64) return false;
        foreach (var c in id)
        {
            if (!(char.IsLetterOrDigit(c) || c == '-')) return false;
        }
        return true;
    }
}
