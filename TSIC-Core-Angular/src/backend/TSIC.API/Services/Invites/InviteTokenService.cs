using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace TSIC.API.Services.Invites;

/// <summary>
/// Mints and validates single-purpose, signed registration-invite tokens.
///
/// The token binds ONE user to ONE target job with a short expiry. It is emitted per recipient
/// in batch-invite emails and enforced server-side at the wizard-entry chokepoints
/// (team <c>InitializeRegistrationAsync</c>, player <c>set-wizard-context</c>) — so a token-gated
/// event can only be entered by the exact person the director invited, within the chosen window.
/// It reuses the same HMAC signing key as the login JWTs (<c>JwtSettings:SecretKey</c>); the
/// <c>purpose</c> claim keeps it from ever being accepted as an auth token and vice-versa.
/// </summary>
public interface IInviteTokenService
{
    /// <summary>Mint a signed invite for <paramref name="invitedUserId"/> into <paramref name="targetJobId"/>, valid until <paramref name="expires"/>.</summary>
    string Create(Guid targetJobId, string invitedUserId, DateTime expires);

    /// <summary>
    /// True only when <paramref name="token"/> is a validly-signed, unexpired invite whose purpose,
    /// target job, and subject match exactly this <paramref name="targetJobId"/> and <paramref name="userId"/>.
    /// Any failure (missing, tampered, expired, wrong job, wrong user) returns false — never throws.
    /// </summary>
    bool IsValidFor(string? token, Guid targetJobId, string userId);
}

public sealed class InviteTokenService : IInviteTokenService
{
    // Distinguishes an invite token from a login JWT signed with the same key. Both the mint and the
    // check assert this claim, so neither token type can be replayed as the other.
    private const string PurposeClaim = "purpose";
    private const string PurposeValue = "registration-invite";
    private const string TargetJobClaim = "targetJobId";

    private readonly IConfiguration _configuration;

    public InviteTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string Create(Guid targetJobId, string invitedUserId, DateTime expires)
    {
        if (string.IsNullOrEmpty(invitedUserId))
            throw new ArgumentException("invitedUserId is required", nameof(invitedUserId));

        var (issuer, audience, secretKey) = GetSettings();

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, invitedUserId),
            new Claim(PurposeClaim, PurposeValue),
            new Claim(TargetJobClaim, targetJobId.ToString("D")),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expires.ToUniversalTime(),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public bool IsValidFor(string? token, Guid targetJobId, string userId)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrEmpty(userId))
            return false;

        var (issuer, audience, secretKey) = GetSettings();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));

        try
        {
            var principal = new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            }, out _);

            // Purpose must be an invite (not a login JWT that happens to share the signing key).
            if (principal.FindFirst(PurposeClaim)?.Value != PurposeValue)
                return false;

            // Subject — ASP.NET remaps `sub` to NameIdentifier during validation, so check both.
            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                   ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.Equals(sub, userId, StringComparison.Ordinal))
                return false;

            // Target job must be exactly the one being entered.
            if (!Guid.TryParse(principal.FindFirst(TargetJobClaim)?.Value, out var tokenJobId)
                || tokenJobId != targetJobId)
                return false;

            return true;
        }
        catch
        {
            // Bad signature, expired, malformed — all mean "not a valid invite".
            return false;
        }
    }

    private (string issuer, string audience, string secretKey) GetSettings()
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");
        var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey not configured");
        var issuer = jwtSettings["Issuer"] ?? "TSIC.API";
        var audience = jwtSettings["Audience"] ?? "TSIC.Client";
        return (issuer, audience, secretKey);
    }
}
