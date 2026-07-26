using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using TSIC.Contracts.Configuration;
using TSIC.Contracts.Services;

namespace TSIC.Infrastructure.Services;

/// <summary>
/// Validates, downscales (ImageSharp), and stores registrant headshots on the statics
/// file share as {userId}.jpg. Combines the userId-keying + path-traversal guard +
/// atomic temp-file write of <see cref="TSIC.Contracts.Services.IMedFormService"/> with the
/// ImageSharp decode/resize/encode of JobImageService. Always re-encodes to JPEG so a
/// user-supplied image can never bloat the public share, and so downstream readers can
/// rely on the {userId}.jpg convention.
/// </summary>
public sealed class HeadshotService : IHeadshotService
{
    private const long MaxBytes = 5L * 1024 * 1024; // 5 MB
    private const int MaxDimension = 800;           // fit within 800x800, aspect preserved
    private const int JpegQuality = 85;

    private readonly string _path;
    private readonly ILogger<HeadshotService> _logger;

    public HeadshotService(IOptions<FileStorageOptions> options, ILogger<HeadshotService> logger)
    {
        _path = options.Value.HeadshotsPath
            ?? throw new InvalidOperationException("FileStorage:HeadshotsPath is not configured.");
        _logger = logger;
        if (!Directory.Exists(_path))
            Directory.CreateDirectory(_path);
    }

    public bool Exists(string userId)
    {
        if (!IsValidUserId(userId)) return false;
        return File.Exists(GetFilePath(userId));
    }

    public async Task<HeadshotUploadResult> UploadAsync(
        string userId, Stream content, long length, CancellationToken ct = default)
    {
        if (!IsValidUserId(userId))
            return new HeadshotUploadResult { Status = HeadshotUploadStatus.InvalidUserId };

        if (length <= 0 || length > MaxBytes)
            return new HeadshotUploadResult { Status = HeadshotUploadStatus.TooLarge };

        // Decoding IS the validation — ImageSharp only loads a genuine JPEG/PNG/WebP, so a
        // renamed/forged file (or non-image) throws here regardless of client Content-Type.
        Image image;
        try
        {
            image = await Image.LoadAsync(content, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new HeadshotUploadResult { Status = HeadshotUploadStatus.InvalidImage };
        }

        try
        {
            // Only downscale — never upscale a smaller headshot.
            if (image.Width > MaxDimension || image.Height > MaxDimension)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(MaxDimension, MaxDimension) // fit within box, aspect preserved
                }));
            }

            var finalPath = GetFilePath(userId);
            // Random temp name (NOT {userId}.*) so the prior-variant sweep below can't hit it.
            var tempPath = Path.Combine(_path, Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                await image.SaveAsJpegAsync(tempPath, new JpegEncoder { Quality = JpegQuality }, ct);

                // Remove any prior variants for this user (e.g. a legacy .png) so only {userId}.jpg remains.
                DeletePriorVariants(userId);

                // Atomic replace of the canonical {userId}.jpg.
                File.Move(tempPath, finalPath, overwrite: true);
            }
            catch
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                throw;
            }

            _logger.LogInformation("Headshot uploaded for user {UserId} ({Bytes} bytes in).", userId, length);
            return new HeadshotUploadResult { Status = HeadshotUploadStatus.Ok };
        }
        finally
        {
            image.Dispose();
        }
    }

    public Task<bool> DeleteAsync(string userId, CancellationToken ct = default)
    {
        if (!IsValidUserId(userId)) return Task.FromResult(false);
        var deleted = DeletePriorVariants(userId);
        if (deleted) _logger.LogInformation("Headshot deleted for user {UserId}.", userId);
        return Task.FromResult(deleted);
    }

    private string GetFilePath(string userId) => Path.Combine(_path, $"{userId}.jpg");

    /// <summary>Deletes every {userId}.* file. Returns true if at least one was removed.</summary>
    private bool DeletePriorVariants(string userId)
    {
        if (!Directory.Exists(_path)) return false;
        var any = false;
        foreach (var file in Directory.EnumerateFiles(_path, $"{userId}.*"))
        {
            File.Delete(file);
            any = true;
        }
        return any;
    }

    // Defense in depth: never let a request escape the configured directory via
    // path-traversal segments in the userId. Identity userIds are GUIDs, so we
    // accept only standard-format characters.
    private static bool IsValidUserId(string? id)
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
