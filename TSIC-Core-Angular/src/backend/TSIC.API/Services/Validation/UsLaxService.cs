using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace TSIC.API.Services.Validation;

public sealed class UsLaxService : IUsLaxService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly IOptions<UsLaxSettings> _options;
    private const string AccessTokenCacheKey = "uslax:access_token";
    private const string AccessTokenExpiryKey = "uslax:access_token_exp";

    public UsLaxService(IHttpClientFactory httpClientFactory, IMemoryCache cache, IOptions<UsLaxSettings> options)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _options = options;
    }

    public async Task<string?> GetMemberRawJsonAsync(string membershipId, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("uslax");
        var token = await GetValidAccessTokenAsync(client, ct);
        if (string.IsNullOrWhiteSpace(token)) return null;

        var content = await SendMemberPingAsync(client, token, membershipId, ct);
        if (content is null || IsBearerTokenInvalid(content))
        {
            // Try one refresh by forcing new token
            token = await FetchAccessTokenAsync(client, ct);
            if (string.IsNullOrWhiteSpace(token)) return content; // return whatever we had
            content = await SendMemberPingAsync(client, token, membershipId, ct);
        }
        return content;
    }

    private async Task<string?> GetValidAccessTokenAsync(HttpClient client, CancellationToken ct)
    {
        if (_cache.TryGetValue<string>(AccessTokenCacheKey, out var token) &&
            _cache.TryGetValue<DateTimeOffset>(AccessTokenExpiryKey, out var exp) &&
            DateTimeOffset.UtcNow < exp.AddMinutes(-1))
        {
            return token;
        }
        return await FetchAccessTokenAsync(client, ct);
    }

    private async Task<string?> FetchAccessTokenAsync(HttpClient client, CancellationToken ct)
    {
    var (clientId, secret, username, password, _) = ResolveCredentials();
        // Use password grant; refresh flow can be added later if needed
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth2/")
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string?, string?>("client_id", clientId),
                new("secret", secret),
                new("username", username),
                new("password", password),
                new("grant_type", "password")
            })
        };
        request.Headers.UserAgent.ParseAdd("TSIC.API/1.0");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var access = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            var expiresInSeconds = root.TryGetProperty("expires_in", out var expEl) ? expEl.GetInt32() : 900;
            if (string.IsNullOrWhiteSpace(access)) return null;
            var exp = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);
            _cache.Set(AccessTokenCacheKey, access, exp);
            _cache.Set(AccessTokenExpiryKey, exp, exp);
            return access;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> SendMemberPingAsync(HttpClient client, string accessToken, string membershipId, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/MemberPing?membership_id={Uri.EscapeDataString(membershipId)}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.UserAgent.ParseAdd("TSIC.API/1.0");
        using var response = await client.SendAsync(req, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static bool IsBearerTokenInvalid(string body)
    {
        if (string.IsNullOrEmpty(body)) return false;
        if (!body.Contains("\"status_code\":500")) return false;
        return body.Contains("Invalid Bearer token") || body.Contains("Bearer token expired");
    }

    private (string clientId, string secret, string username, string password, string? refresh) ResolveCredentials()
    {
        var s = _options.Value;
        string clientId = s.ClientId ?? Environment.GetEnvironmentVariable("USLAX_CLIENT_ID") ?? string.Empty;
        string secret = s.Secret ?? Environment.GetEnvironmentVariable("USLAX_SECRET") ?? string.Empty;
        string username = s.Username ?? Environment.GetEnvironmentVariable("USLAX_USERNAME") ?? string.Empty;
        string password = s.Password ?? Environment.GetEnvironmentVariable("USLAX_PASSWORD") ?? string.Empty;
        string? refresh = s.RefreshToken ?? Environment.GetEnvironmentVariable("USLAX_REFRESH_TOKEN");
        return (clientId, secret, username, password, refresh);
    }
}
