using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using TSIC.Contracts.Configuration;
using TSIC.Contracts.Services;

namespace TSIC.Infrastructure.Services;

/// <summary>
/// Validates, resizes (ImageSharp), and stores job branding images on the statics file share.
/// Follows the legacy naming convention: {jobId}_{conventionName}.{ext} (all lowercase).
/// </summary>
public class JobImageService : IJobImageService
{
    private readonly string _bannerFilesPath;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private const long MaxFileSize = 5 * 1024 * 1024; // 5 MB

    public JobImageService(IOptions<FileStorageOptions> options)
    {
        _bannerFilesPath = options.Value.BannerFilesPath;
    }

    public async Task<string> SaveJobImageAsync(
        Stream fileStream,
        string originalFileName,
        Guid jobId,
        string conventionName,
        int maxWidth,
        int quality,
        CancellationToken ct = default)
    {
        var ext = Path.GetExtension(originalFileName)?.ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            throw new ArgumentException($"File type '{ext}' is not allowed. Allowed: JPG, PNG, WebP.");

        if (fileStream.Length > MaxFileSize)
            throw new ArgumentException($"File exceeds the {MaxFileSize / (1024 * 1024)}MB limit.");

        // Delete old files matching this convention
        await DeleteJobImageAsync(jobId, conventionName, ct);

        // Load, resize if needed, and save
        using var image = await Image.LoadAsync(fileStream, ct);

        if (image.Width > maxWidth)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(maxWidth, 0) // height auto-calculated to maintain aspect ratio
            }));
        }

        var newFileName = $"{jobId}_{conventionName}{ext}".ToLowerInvariant();
        var savePath = Path.Combine(_bannerFilesPath, newFileName);

        // Save in original format — PNG stays PNG (preserves transparency), JPG/WebP get quality control
        if (ext is ".png")
        {
            await image.SaveAsPngAsync(savePath, ct);
        }
        else if (ext is ".webp")
        {
            await image.SaveAsWebpAsync(savePath, new WebpEncoder { Quality = quality }, ct);
        }
        else
        {
            // .jpg / .jpeg
            await image.SaveAsJpegAsync(savePath, new JpegEncoder { Quality = quality }, ct);
        }

        return newFileName;
    }

    public Task DeleteJobImageAsync(Guid jobId, string conventionName, CancellationToken ct = default)
    {
        var pattern = $"{jobId}_{conventionName}.*".ToLowerInvariant();

        if (Directory.Exists(_bannerFilesPath))
        {
            foreach (var file in Directory.EnumerateFiles(_bannerFilesPath, pattern))
            {
                File.Delete(file);
            }
        }

        return Task.CompletedTask;
    }
}
