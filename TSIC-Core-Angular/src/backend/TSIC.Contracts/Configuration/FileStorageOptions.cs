namespace TSIC.Contracts.Configuration;

/// <summary>
/// Configuration for physical file storage paths (mirrors legacy Production_BannerFilesPath pattern).
/// </summary>
public class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>
    /// Physical path to the BannerFiles directory served by statics.teamsportsinfo.com.
    /// </summary>
    public required string BannerFilesPath { get; init; }
}
