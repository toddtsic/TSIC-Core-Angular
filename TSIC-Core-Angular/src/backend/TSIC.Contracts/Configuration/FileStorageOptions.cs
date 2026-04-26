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

    /// <summary>
    /// Physical path to the MedForms directory. Mirrors the legacy
    /// Production_RegistrationUploadFilesPath\MedForms location so existing
    /// uploaded files keep working without migration. Files are named
    /// {playerUserId}.pdf — global to the person, persisted across jobs.
    /// </summary>
    public required string MedFormsPath { get; init; }
}
