namespace TSIC.Contracts.Services;

/// <summary>
/// Per-player medical-form PDF storage. Files are keyed by the player's
/// identity userId — global to the person, persisted across jobs and
/// re-registrations. Mirrors legacy MedForms\{userId}.pdf convention so
/// existing files keep working without migration.
/// </summary>
public interface IMedFormService
{
    /// <summary>
    /// Stores the uploaded PDF as {playerUserId}.pdf. Validates magic bytes
    /// and size cap; overwrites any existing file atomically.
    /// </summary>
    Task<MedFormUploadResult> UploadAsync(
        string playerUserId,
        Stream content,
        long length,
        CancellationToken ct = default);

    /// <summary>
    /// Returns an open read stream for the player's med form, or null if no
    /// file exists. Caller is responsible for disposing the stream.
    /// </summary>
    Task<Stream?> ReadAsync(string playerUserId, CancellationToken ct = default);

    /// <summary>
    /// Deletes the player's med form. Returns true if a file was deleted,
    /// false if none existed.
    /// </summary>
    Task<bool> DeleteAsync(string playerUserId, CancellationToken ct = default);

    /// <summary>
    /// True iff a med form file currently exists for the player. Used by
    /// PlayerRegistrationService at row-creation time to stamp the
    /// Registrations.BUploadedMedForm flag from disk state, never from a
    /// client-asserted value.
    /// </summary>
    bool Exists(string playerUserId);
}

public enum MedFormUploadStatus
{
    Ok,
    InvalidPdf,
    TooLarge,
    InvalidPlayerUserId,
}

public record MedFormUploadResult
{
    public required MedFormUploadStatus Status { get; init; }
    public string? Error { get; init; }
}
