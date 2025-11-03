using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace TSIC.API.Services;

/// <summary>
/// GitHub API response for file contents
/// </summary>
public class GitHubFileContent
{
    public string name { get; set; } = string.Empty;
    public string path { get; set; } = string.Empty;
    public string sha { get; set; } = string.Empty;
    public long size { get; set; }
    public string content { get; set; } = string.Empty;
    public string encoding { get; set; } = string.Empty;
}

/// <summary>
/// Fetches POCO class source files from GitHub repository
/// </summary>
public class GitHubProfileFetcher
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GitHubProfileFetcher> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _repoBranch;

    private const string BaseClassFileName = "BaseRegForm_ViewModels.cs";
    private const string CacheKeyPrefix = "GitHub_";
    private const string TargetRepoName = "TSIC-Unify-2024";

    public GitHubProfileFetcher(
        HttpClient httpClient,
        IMemoryCache cache,
        ILogger<GitHubProfileFetcher> logger,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
        _configuration = configuration;

        // Branch used when querying TSIC-Unify-2024 (configurable)
        _repoBranch = _configuration["GitHub:RepoBranch"] ?? "master2025";
        _logger.LogInformation("Using GitHub repo branch: {Branch}", _repoBranch);

        // GitHub API requires User-Agent header
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("TSIC-ProfileMigration", "1.0"));

        // Add auth token if configured
        var githubToken = _configuration["GitHub:Token"];
        if (!string.IsNullOrEmpty(githubToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", githubToken);
            _logger.LogInformation("GitHub authentication configured");
        }
        else
        {
            _logger.LogWarning("GitHub Token not configured - can only access public repositories");
        }
    }

    /// <summary>
    /// Fetch profile POCO source code from GitHub
    /// </summary>
    /// <param name="profileType">e.g., "PP10", "PP17", "CAC05"</param>
    public async Task<(string sourceCode, string commitSha)> FetchProfileSourceAsync(string profileType)
    {
        try
        {
            var repoOwner = _configuration["GitHub:RepoOwner"] ?? "toddtsic";
            var repoName = _configuration["GitHub:RepoName"] ?? TargetRepoName;

            // Determine path based on profile type
            string folder;
            string fileName;

            if (profileType.StartsWith("CAC"))
            {
                folder = "RegPlayersMulti_ViewModels";
                // CAC files are named like "CAC04ViewModels.cs" (plural)
                fileName = $"{profileType}ViewModels.cs";
            }
            else // PP profiles
            {
                folder = "RegPlayersSingle_ViewModels";
                // PP files are named like "PP10ViewModel.cs"
                fileName = $"{profileType}ViewModel.cs";
            }

            var path = $"TSIC-Unify-Models/ViewModels/{folder}/{fileName}";

            _logger.LogInformation("Fetching {ProfileType} from GitHub: {Path}", profileType, path);

            var content = await FetchFileContentAsync(repoOwner, repoName, path);

            return (DecodeBase64Content(content.content), content.sha);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch profile source for {ProfileType}", profileType);
            throw new InvalidOperationException($"Could not fetch {profileType} from GitHub: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Fetch base demographics class (cached)
    /// </summary>
    public async Task<(string sourceCode, string commitSha)> FetchBaseClassSourceAsync()
    {
        // Include branch in cache key to avoid cross-branch pollution
        var cacheKey = $"{CacheKeyPrefix}{_repoBranch}_{BaseClassFileName}";

        if (_cache.TryGetValue<(string, string)>(cacheKey, out var cached))
        {
            _logger.LogDebug("Using cached base class source");
            return cached;
        }

        try
        {
            var repoOwner = _configuration["GitHub:RepoOwner"] ?? "toddtsic";
            var repoName = _configuration["GitHub:RepoName"] ?? TargetRepoName;
            var path = $"TSIC-Unify-Models/ViewModels/RegForm_ViewModels/{BaseClassFileName}";

            _logger.LogInformation("Fetching base class from GitHub: {Path}", path);

            var content = await FetchFileContentAsync(repoOwner, repoName, path);
            var result = (DecodeBase64Content(content.content), content.sha);

            // Cache for 1 hour (base class rarely changes)
            _cache.Set(cacheKey, result, TimeSpan.FromHours(1));

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch base class source");
            throw new InvalidOperationException($"Could not fetch base class from GitHub: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Fetch .cshtml view file for a profile to extract hidden fields
    /// </summary>
    /// <param name="profileType">e.g., "PP10", "PP17", "CAC05"</param>
    public async Task<string?> FetchViewFileAsync(string profileType)
    {
        try
        {
            var repoOwner = _configuration["GitHub:RepoOwner"] ?? "toddtsic";
            var repoName = _configuration["GitHub:RepoName"] ?? TargetRepoName;

            // Determine path based on profile type
            string folder = profileType.StartsWith("CAC") ? "PlayerMulti" : "PlayerSingle";
            var path = $"TSIC-Unify/Views/PlayerRegistrationForms/{folder}/{profileType}.cshtml";

            _logger.LogInformation("Fetching view file for {ProfileType} from GitHub: {Path}", profileType, path);

            var content = await FetchFileContentAsync(repoOwner, repoName, path);
            return DecodeBase64Content(content.content);
        }
        catch (Exception ex)
        {
            // View file is optional - if it doesn't exist, we'll use default behavior
            _logger.LogWarning(ex, "Could not fetch view file for {ProfileType} - will use default hidden field detection", profileType);
            return null;
        }
    }

    private async Task<GitHubFileContent> FetchFileContentAsync(string owner, string repo, string path)
    {
        var baseUrl = $"https://api.github.com/repos/{owner}/{repo}/contents/{path}";
        // Only enforce branch for TSIC-Unify-2024 repository
        var url = string.Equals(repo, TargetRepoName, StringComparison.OrdinalIgnoreCase)
            ? $"{baseUrl}?ref={Uri.EscapeDataString(_repoBranch)}"
            : baseUrl;

        _logger.LogDebug("Requesting GitHub URL: {Url}", url);
        _logger.LogDebug("Path components - Owner: {Owner}, Repo: {Repo}, Path: {Path}, Branch: {Branch}", owner, repo, path, string.Equals(repo, TargetRepoName, StringComparison.OrdinalIgnoreCase) ? _repoBranch : "(default)");

        var response = await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("GitHub API request failed. URL: {Url}, Status: {Status}, Response: {Response}",
                url, response.StatusCode, error);
            throw new HttpRequestException(
                $"GitHub API returned {response.StatusCode}: {error}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var content = JsonSerializer.Deserialize<GitHubFileContent>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (content == null)
        {
            throw new InvalidOperationException("Failed to deserialize GitHub response");
        }

        return content;
    }

    private static string DecodeBase64Content(string base64Content)
    {
        // GitHub returns content with newlines in the base64 string
        var cleanedBase64 = base64Content.Replace("\n", "").Replace("\r", "");
        var bytes = Convert.FromBase64String(cleanedBase64);
        return Encoding.UTF8.GetString(bytes);
    }
}
