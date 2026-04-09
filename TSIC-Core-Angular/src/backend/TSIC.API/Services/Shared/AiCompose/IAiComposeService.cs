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
}
