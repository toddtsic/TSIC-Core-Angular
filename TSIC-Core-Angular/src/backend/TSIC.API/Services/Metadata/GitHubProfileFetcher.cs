using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace TSIC.API.Services.Metadata;

/// <summary>
/// Fetches POCO class source files from local git submodule
/// </summary>
public class GitHubProfileFetcher : IGitHubProfileFetcher
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<GitHubProfileFetcher> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly string _repoBasePath;

    private const string BaseClassFileName = "BaseRegForm_ViewModels.cs";
    private const string CacheKeyPrefix = "LocalProfile_";

    public GitHubProfileFetcher(
        IMemoryCache cache,
        ILogger<GitHubProfileFetcher> logger,
        IWebHostEnvironment environment)
    {
        _cache = cache;
        _logger = logger;
        _environment = environment;

        // Navigate from API project to repo root: ../../../../ then to reference/TSIC-Unify-2024
        var apiPath = _environment.ContentRootPath; // C:\...\TSIC-Core-Angular\src\backend\TSIC.API
        var backendPath = Path.GetDirectoryName(apiPath); // C:\...\TSIC-Core-Angular\src\backend
        var srcPath = Path.GetDirectoryName(backendPath); // C:\...\TSIC-Core-Angular\src
        var innerRoot = Path.GetDirectoryName(srcPath); // C:\...\TSIC-Core-Angular\TSIC-Core-Angular
        var repoRoot = Path.GetDirectoryName(innerRoot); // C:\...\TSIC-Core-Angular
        _repoBasePath = Path.Combine(repoRoot!, "reference", "TSIC-Unify-2024");

        if (!Directory.Exists(_repoBasePath))
        {
            _logger.LogError("Local reference repository not found at: {Path}", _repoBasePath);
            throw new DirectoryNotFoundException($"TSIC-Unify-2024 submodule not found at {_repoBasePath}");
        }

        _logger.LogInformation("Using local repository at: {Path}", _repoBasePath);
    }

    /// <summary>
    /// Fetch profile POCO source code from local submodule
    /// </summary>
    /// <param name="profileType">e.g., "PP10", "PP17", "CAC05"</param>
    public async Task<(string sourceCode, string commitSha)> FetchProfileSourceAsync(string profileType)
    {
        var cacheKey = $"{CacheKeyPrefix}{profileType}";

        if (_cache.TryGetValue<(string, string)>(cacheKey, out var cached))
        {
            _logger.LogDebug("Using cached profile source for {ProfileType}", profileType);
            return cached;
        }

        try
        {
            string folder;
            string fileName;

            if (profileType.StartsWith("CAC"))
            {
                folder = "RegPlayersMulti_ViewModels";
                fileName = $"{profileType}ViewModels.cs"; // plural convention (CAC04ViewModels.cs)
            }
            else // PP profiles
            {
                folder = "RegPlayersSingle_ViewModels";
                fileName = $"{profileType}ViewModel.cs";
            }

            var filePath = Path.Combine(_repoBasePath, "TSIC-Unify-Models", "ViewModels", folder, fileName);

            // Fallback: try singular ViewModel.cs if plural ViewModels.cs not found (some CAC files use singular)
            if (!File.Exists(filePath) && profileType.StartsWith("CAC"))
            {
                var fallbackFileName = $"{profileType}ViewModel.cs";
                var fallbackPath = Path.Combine(_repoBasePath, "TSIC-Unify-Models", "ViewModels", folder, fallbackFileName);
                if (File.Exists(fallbackPath))
                {
                    _logger.LogInformation("Using singular ViewModel fallback for {ProfileType}: {Path}", profileType, fallbackPath);
                    filePath = fallbackPath;
                }
            }

            _logger.LogInformation("Fetching {ProfileType} from local file: {Path}", profileType, filePath);

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Profile source file not found: {filePath}");
            }

            var sourceCode = await File.ReadAllTextAsync(filePath);
            var commitSha = ComputeFileHash(filePath);

            var result = (sourceCode, commitSha);

            // Cache for 5 minutes (files won't change during development session)
            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch profile source for {ProfileType}", profileType);
            throw new InvalidOperationException($"Could not fetch {profileType} from local repository: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Fetch base demographics class (cached)
    /// </summary>
    public async Task<(string sourceCode, string commitSha)> FetchBaseClassSourceAsync()
    {
        var cacheKey = $"{CacheKeyPrefix}{BaseClassFileName}";

        if (_cache.TryGetValue<(string, string)>(cacheKey, out var cached))
        {
            _logger.LogDebug("Using cached base class source");
            return cached;
        }

        try
        {
            var filePath = Path.Combine(_repoBasePath, "TSIC-Unify-Models", "ViewModels", "RegForm_ViewModels", BaseClassFileName);

            _logger.LogInformation("Fetching base class from local file: {Path}", filePath);

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Base class file not found: {filePath}");
            }

            var sourceCode = await File.ReadAllTextAsync(filePath);
            var commitSha = ComputeFileHash(filePath);

            var result = (sourceCode, commitSha);

            // Cache for 1 hour (base class rarely changes)
            _cache.Set(cacheKey, result, TimeSpan.FromHours(1));

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch base class source");
            throw new InvalidOperationException($"Could not fetch base class from local repository: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// List all profile types (PP## and CAC##) by scanning local directories
    /// </summary>
    public async Task<List<string>> ListAllProfileTypesAsync()
    {
        var ppPath = Path.Combine(_repoBasePath, "TSIC-Unify-Models", "ViewModels", "RegPlayersSingle_ViewModels");
        var cacPath = Path.Combine(_repoBasePath, "TSIC-Unify-Models", "ViewModels", "RegPlayersMulti_ViewModels");

        var result = new List<string>();

        // PP files are like PP10ViewModel.cs
        if (Directory.Exists(ppPath))
        {
            foreach (var file in Directory.GetFiles(ppPath, "PP*ViewModel.cs"))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var m = System.Text.RegularExpressions.Regex.Match(fileName, @"^(PP\d+)ViewModel$");
                if (m.Success) result.Add(m.Groups[1].Value);
            }
        }

        // CAC files are like CAC04ViewModels.cs (plural) or CAC18ViewModel.cs (singular)
        if (Directory.Exists(cacPath))
        {
            foreach (var file in Directory.GetFiles(cacPath, "CAC*ViewModel*.cs"))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var m = System.Text.RegularExpressions.Regex.Match(fileName, @"^(CAC\d+)ViewModels?$");
                if (m.Success) result.Add(m.Groups[1].Value);
            }
        }

        return await Task.FromResult(result.Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x)
                     .ToList());
    }
    /// <summary>
    /// Fetch .cshtml view file for a profile to extract hidden fields
    /// </summary>
    /// <param name="profileType">e.g., "PP10", "PP17", "CAC05"</param>
    public async Task<string?> FetchViewFileAsync(string profileType)
    {
        try
        {
            string folder = profileType.StartsWith("CAC") ? "PlayerMulti" : "PlayerSingle";
            var filePath = Path.Combine(_repoBasePath, "TSIC-Unify", "Views", "PlayerRegistrationForms", folder, $"{profileType}.cshtml");

            _logger.LogInformation("Fetching view file for {ProfileType} from local file: {Path}", profileType, filePath);

            if (!File.Exists(filePath))
            {
                _logger.LogWarning("View file not found for {ProfileType} at {Path}", profileType, filePath);
                return null;
            }

            return await File.ReadAllTextAsync(filePath);
        }
        catch (Exception ex)
        {
            // View file is optional - if it doesn't exist, we'll use default behavior
            _logger.LogWarning(ex, "Could not fetch view file for {ProfileType} - will use default hidden field detection", profileType);
            return null;
        }
    }

    /// <summary>
    /// Compute a simple hash of the file for versioning/tracking
    /// </summary>
    private string ComputeFileHash(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()[..16]; // First 16 chars like Git
    }
}
