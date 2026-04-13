using TSIC.Contracts.Dtos.Email;

namespace TSIC.API.Services.Shared.AiCompose;

public interface IAiComposeService
{
    Task<AiComposeResponse> ComposeEmailAsync(
        Guid jobId,
        string prompt,
        CancellationToken ct = default);

    Task<AiComposeResponse> ComposeBulletinAsync(
        Guid jobId,
        string prompt,
        CancellationToken ct = default);

    /// <summary>
    /// Reformat existing bulletin HTML for better UX using design-system classes
    /// and, where semantically appropriate, insert !TOKEN markers from the
    /// bulletin vocabulary. Returns raw HTML (no subject/body split).
    /// </summary>
    Task<AiFormatResponse> FormatBulletinAsync(
        Guid jobId,
        string existingHtml,
        CancellationToken ct = default);
}
