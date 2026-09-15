using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace TSIC.API.Services.Invites;

/// <summary>What an invite admits its recipient to. A token minted for one purpose never validates for another.</summary>
public enum InvitePurpose
{
    /// <summary>Enter a registration wizard for the target job (player or club rep pre-registration).</summary>
    Registration,

    /// <summary>View the target job's schedule before it is released to the public (club reps).</summary>
    SchedulePreview
}

/// <summary>
/// Mints and validates single-purpose, signed invite tokens.
///
/// The token binds ONE user to ONE target job for ONE purpose with a short expiry. It is emitted per
/// recipient in batch-invite emails and enforced server-side against the recipient's own login — a
/// registration invite at the wizard-entry chokepoints (team <c>InitializeRegistrationAsync</c>, player
/// <c>set-wizard-context</c>), a schedule-preview invite in <c>ViewScheduleService.CanViewScheduleAsync</c>.
/// It reuses the same HMAC signing key as the login JWTs (<c>JwtSettings:SecretKey</c>); the
/// <c>purpose</c> claim keeps it from ever being accepted as an auth token and vice-versa.
/// </summary>
public interface IInviteTokenService
{
    /// <summary>Mint a signed <paramref name="purpose"/> invite for <paramref name="invitedUserId"/> into <paramref name="targetJobId"/>, valid until <paramref name="expires"/>.</summary>
    string Create(InvitePurpose purpose, Guid targetJobId, string invitedUserId, DateTime expires);

    /// <summary>
    /// True only when <paramref name="token"/> is a validly-signed, unexpired invite whose purpose,
    /// target job, and subject match exactly <paramref name="purpose"/>, <paramref name="targetJobId"/> and <paramref name="userId"/>.
    /// Any failure (missing, tampered, expired, wrong purpose, wrong job, wrong user) returns false — never throws.
    /// </summary>
    bool IsValidFor(InvitePurpose purpose, string? token, Guid targetJobId, string userId);
}

public sealed class InviteTokenService : IInviteTokenService
{
    // Distinguishes an invite token from a login JWT signed with the same key. The mint and IsValidFor
    // assert it here; the JWT bearer handler (Program.cs OnTokenValidated) refuses any token carrying it,
    // so an invite can never stand in for the login it is checked against.
    public const string PurposeClaim = "purpose";
    private const string TargetJobClaim = "targetJobId";

    // Claim values. "registration-invite" predates the purpose enum — already-emailed invites carry it.
    private static string ClaimValue(InvitePurpose purpose) => purpose switch
    {
        InvitePurpose.Registration => "registration-invite",
        InvitePurpose.SchedulePreview => "schedule-preview",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null)
    };

    private readonly IConfiguration _configuration;

    public InviteTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// JWT bearer <c>OnTokenValidated</c> hook, wired in Program.cs. Invites share the login key, issuer
    /// and audience, so standard validation accepts them; this refuses any token carrying the purpose claim.
    /// Without it an emailed invite link was a login as its recipient until expiry.
    /// </summary>
    public static Task RefuseAsLogin(TokenValidatedContext context)
    {
        if (context.Principal?.FindFirst(PurposeClaim) != null)
            context.Fail("Invite tokens are not accepted as login tokens.");
        return Task.CompletedTask;
    }

    public string Create(InvitePurpose purpose, Guid targetJobId, string invitedUserId, DateTime expires)
    {
        if (string.IsNullOrEmpty(invitedUserId))
            throw new ArgumentException("invitedUserId is required", nameof(invitedUserId));

        var (issuer, audience, secretKey) = GetSettings();

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, invitedUserId),
            new Claim(PurposeClaim, ClaimValue(purpose)),
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

    public bool IsValidFor(InvitePurpose purpose, string? token, Guid targetJobId, string userId)
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

            // Purpose must be exactly this invite kind (not a login JWT sharing the key, nor another invite kind).
            if (principal.FindFirst(PurposeClaim)?.Value != ClaimValue(purpose))
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
