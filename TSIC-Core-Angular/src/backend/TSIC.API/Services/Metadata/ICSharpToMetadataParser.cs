using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Metadata;

public interface ICSharpToMetadataParser
{
    Task<ProfileMetadata> ParseProfileAsync(
        string profileSourceCode,
        string baseClassSourceCode,
        string profileType,
        string commitSha,
        string? viewContent = null);
}
