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

    /// <summary>
    /// Where the ADN month-end close artifacts (bundle.zip + ledger.json + meta.json) are persisted so
    /// the sprocs run once per pull, not once per wizard step. A relative value is resolved against the
    /// app's ContentRoot; unset defaults to <c>{ContentRoot}/App_Data/AdnMonthEnd</c>. This location is
    /// deliberately NOT web-served — files are streamed only through the Superuser-gated controller.
    /// </summary>
    public string? MonthEndExportPath { get; init; }

    /// <summary>
    /// Where context-sensitive help content lives, as HTML fragments under
    /// <c>{HelpContentPath}/{component}/{topic}.html</c>. A relative value resolves against the app's
    /// ContentRoot; unset defaults to <c>{ContentRoot}/App_Data/Help</c>. These files are git-tracked and
    /// deployed with the app. Like the month-end export, this location is NOT web-served — content is read
    /// back only through the anonymous, path-traversal-guarded help controller. SuperUser in-app edits
    /// (sandbox only) write the working-tree file, which is then committed and deployed (Model A).
    /// </summary>
    public string? HelpContentPath { get; init; }
}
