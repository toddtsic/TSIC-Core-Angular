using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TSIC.API.Controllers;

/// <summary>
/// Remote validation endpoints for form fields
/// Public endpoints for registration form validation
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/[controller]")]
public class ValidationController : ControllerBase
{
    private readonly ILogger<ValidationController> _logger;
    private static readonly string UslaxApiBase = Environment.GetEnvironmentVariable("USLAX_API_BASE") ?? "https://api.usalacrosse.com/";
    private static readonly HttpClient _uslaxHttp = new HttpClient { BaseAddress = new Uri(UslaxApiBase) };
    private static readonly object _tokenLock = new();
    private static string? _accessToken;
    private static string? _refreshToken;
    private static DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;

    public ValidationController(ILogger<ValidationController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validate USA Lacrosse membership ID via their API
    /// </summary>
    /// <param name="sportAssnID">USA Lacrosse membership number</param>
    /// <returns>True if valid, false otherwise</returns>
    /// <remarks>
    /// Placeholder: format checks only. Real proxy endpoint: GET /api/validation/uslax
    /// </remarks>
    [HttpGet("ValidateUSALacrosseID")]
    public async Task<ActionResult<bool>> ValidateUSALacrosseID([FromQuery] string sportAssnID)
    {
        if (string.IsNullOrWhiteSpace(sportAssnID))
        {
            return BadRequest(new { valid = false, message = "USA Lacrosse ID is required" });
        }

        try
        {
            // For now, just validate format (example: must be numeric and certain length)

            _logger.LogInformation("Validating USA Lacrosse ID: {SportAssnID}", sportAssnID);

            // Placeholder validation logic
            var isNumeric = long.TryParse(sportAssnID, out _);
            var hasValidLength = sportAssnID.Length >= 6 && sportAssnID.Length <= 10;

            if (!isNumeric)
            {
                return Ok(new { valid = false, message = "USA Lacrosse ID must be numeric" });
            }

            if (!hasValidLength)
            {
                return Ok(new { valid = false, message = "USA Lacrosse ID must be between 6 and 10 digits" });
            }

            // Placeholder: Accept all valid-format IDs for now
            return Ok(new { valid = true, message = "USA Lacrosse ID format is valid (API validation not yet implemented)" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating USA Lacrosse ID: {SportAssnID}", sportAssnID);
            return StatusCode(500, new { valid = false, message = "Validation service temporarily unavailable" });
        }
    }

    /// <summary>
    /// Proxy endpoint for USA Lacrosse membership verification using official API.
    /// Returns raw JSON from USALax (or a simplified error JSON) to the client.
    /// Client performs last-name/DOB/expiration checks.
    /// </summary>
    /// <param name="number">Membership number</param>
    /// <param name="lastName">Optional last name (not used server-side)</param>
    /// <param name="dob">Optional DOB (not used server-side)</param>
    /// <param name="validThrough">Optional valid-through date (not used server-side)</param>
    [HttpGet("uslax")]
    public async Task<IActionResult> ValidateUsLax([FromQuery] string number)
    {
        if (string.IsNullOrWhiteSpace(number)) return BadRequest(new { message = "number is required" });
        try
        {
            var accessToken = await GetValidUsLaxAccessToken();
            if (string.IsNullOrWhiteSpace(accessToken))
                return Ok(new { membership = (object?)null, message = "Unable to obtain USLax token" });

            var response = await SendUslaxMemberPing(accessToken, number);
            var content = await response.Content.ReadAsStringAsync();

            if (IsBearerTokenInvalid(content))
            {
                _logger.LogWarning("Bearer token invalid/expired. Attempting refresh.");
                accessToken = await ForceRefreshToken();
                if (!string.IsNullOrWhiteSpace(accessToken))
                {
                    response = await SendUslaxMemberPing(accessToken, number);
                    content = await response.Content.ReadAsStringAsync();
                }
            }

            return Content(content, "application/json", Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "USLax validation failed for number {Number}", number);
            return Ok(new { membership = (object?)null, message = "Validation service temporarily unavailable" });
        }
    }

    private static async Task<(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt)?> FetchUslaxToken(bool useRefresh = false)
    {
        // Prefer environment variables or appsettings; fallback to legacy defaults if present
        string clientId = Environment.GetEnvironmentVariable("USLAX_CLIENT_ID") ?? "9cc2c5e1-e678-461b-867f-82243384ba6f";
        string secret = Environment.GetEnvironmentVariable("USLAX_SECRET") ?? "ac58f81ae1d59e41cb37cd533b86ffff1b4e6bae07c201238a11050337680ef9";
        string username = Environment.GetEnvironmentVariable("USLAX_USERNAME") ?? "teamsportsinfo_prod";
        string password = Environment.GetEnvironmentVariable("USLAX_PASSWORD") ?? "&oEJ1@O6J03Z";
        string? refresh = Environment.GetEnvironmentVariable("USLAX_REFRESH_TOKEN");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth2/")
        {
            Content = useRefresh
                ? new FormUrlEncodedContent(new[] { new KeyValuePair<string?, string?>("refresh_token", refresh), new("grant_type", "refresh_token") })
                : new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string?, string?>("client_id", clientId),
                    new("secret", secret),
                    new("username", username),
                    new("password", password),
                    new("grant_type", "password")
                })
        };
        request.Headers.Add("User-Agent", "PostmanRuntime/7.29.0");

        using var response = await _uslaxHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var access = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            var expiresInSeconds = root.TryGetProperty("expires_in", out var expEl) ? expEl.GetInt32() : 0;
            if (string.IsNullOrWhiteSpace(access)) return null;
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds > 0 ? expiresInSeconds : 900); // default 15m if missing
            return (access, refreshToken, expiresAt);
        }
        catch { return null; }
    }

    private static async Task<HttpResponseMessage> SendUslaxMemberPing(string accessToken, string membershipId)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/MemberPing?membership_id={Uri.EscapeDataString(membershipId)}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.Add("User-Agent", "PostmanRuntime/7.29.0");
        return await _uslaxHttp.SendAsync(req);
    }

    private static async Task<string?> GetValidUsLaxAccessToken()
    {
        // Fast path if token is still valid
        if (!string.IsNullOrWhiteSpace(_accessToken) && DateTimeOffset.UtcNow < _accessTokenExpiresAt.AddMinutes(-1))
            return _accessToken;

        // Acquire lock for refresh/fetch
        lock (_tokenLock)
        {
            if (!string.IsNullOrWhiteSpace(_accessToken) && DateTimeOffset.UtcNow < _accessTokenExpiresAt.AddMinutes(-1))
                return _accessToken; // double-check after lock
        }

        // Try refresh first if we have a refresh token
        if (!string.IsNullOrWhiteSpace(_refreshToken))
        {
            var refreshed = await FetchUslaxToken(useRefresh: true);
            if (refreshed is not null)
            {
                lock (_tokenLock)
                {
                    _accessToken = refreshed.Value.AccessToken;
                    _refreshToken = refreshed.Value.RefreshToken ?? _refreshToken;
                    _accessTokenExpiresAt = refreshed.Value.ExpiresAt;
                }
                return _accessToken;
            }
        }

        // Fallback to password grant
        var token = await FetchUslaxToken();
        if (token is null) return null;
        lock (_tokenLock)
        {
            _accessToken = token.Value.AccessToken;
            _refreshToken = token.Value.RefreshToken;
            _accessTokenExpiresAt = token.Value.ExpiresAt;
        }
        return _accessToken;
    }

    private static async Task<string?> ForceRefreshToken()
    {
        // Always attempt refresh grant first, then password grant if that fails
        var refreshed = await FetchUslaxToken(useRefresh: true) ?? await FetchUslaxToken(useRefresh: false);
        if (refreshed is null) return null;
        lock (_tokenLock)
        {
            _accessToken = refreshed.Value.AccessToken;
            _refreshToken = refreshed.Value.RefreshToken;
            _accessTokenExpiresAt = refreshed.Value.ExpiresAt;
        }
        return _accessToken;
    }

    private static bool IsBearerTokenInvalid(string body)
    {
        if (string.IsNullOrEmpty(body)) return false;
        if (!body.Contains("\"status_code\":500")) return false;
        return body.Contains("Invalid Bearer token") || body.Contains("Bearer token expired");
    }
}

