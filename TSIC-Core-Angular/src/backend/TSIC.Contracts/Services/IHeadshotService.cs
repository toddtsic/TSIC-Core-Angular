namespace TSIC.Contracts.Services;

/// <summary>
/// Per-registrant headshot storage. Files are keyed by the registrant's
/// identity userId — global to the person, persisted across jobs and
/// re-registrations. Stored as {userId}.jpg in the fresh Headshots-AllRegistrants
/// statics folder (public-by-GUID), always re-encoded to a downscaled JPEG so a
/// user-supplied image can never bloat the share.
/// </summary>
public interface IHeadshotService
{
    /// <summary>
    /// Decodes the uploaded image (any of JPEG/PNG/WebP), downscales it to fit the
    /// max dimension, and stores it as {userId}.jpg — overwriting any prior headshot
    /// atomically. Validates the byte content is a real image and the size cap;
    /// rejects anything that fails to decode.
    /// </summary>
    Task<HeadshotUploadResult> UploadAsync(
        string userId,
        Stream content,
        long length,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes the registrant's headshot. Returns true if a file was deleted,
    /// false if none existed.
    /// </summary>
    Task<bool> DeleteAsync(string userId, CancellationToken ct = default);

    /// <summary>True iff a headshot file currently exists for the registrant.</summary>
    bool Exists(string userId);
}

public enum HeadshotUploadStatus
{
    Ok,
    InvalidImage,
    TooLarge,
    InvalidUserId,
}

public record HeadshotUploadResult
{
    public required HeadshotUploadStatus Status { get; init; }
    public string? Error { get; init; }
}
