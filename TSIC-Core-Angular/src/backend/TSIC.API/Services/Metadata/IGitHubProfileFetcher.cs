namespace TSIC.API.Services.Metadata;

public interface IGitHubProfileFetcher
{
    Task<(string sourceCode, string commitSha)> FetchProfileSourceAsync(string profileType);
    Task<(string sourceCode, string commitSha)> FetchBaseClassSourceAsync();
    Task<List<string>> ListAllProfileTypesAsync();
    Task<string?> FetchViewFileAsync(string profileType);
}
