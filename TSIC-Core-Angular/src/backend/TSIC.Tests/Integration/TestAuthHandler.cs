using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TSIC.Domain.Constants;

namespace TSIC.Tests.Integration;

/// <summary>
/// Authentication handler used only in integration tests to bypass JWT while still exercising authorization policies.
/// Role is driven by the bearer token value (admin/director/superuser/staff/player).
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        var token = ExtractToken(authHeader);

        var (userId, role) = GetIdentityForToken(token);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Role, role),
            new("regId", "11111111-1111-1111-1111-111111111111"),
            new("jobPath", "integration-job")
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static string ExtractToken(string header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return "admin"; // Default to admin when no header is present
        }

        const string bearerPrefix = "Bearer ";
        if (header.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return header[bearerPrefix.Length..].Trim();
        }

        return header.Trim();
    }

    private static (string userId, string role) GetIdentityForToken(string token)
    {
        return token.ToLowerInvariant() switch
        {
            "player" or "unauthorized" => ("player-user", RoleConstants.Names.PlayerName),
            "staff" => ("staff-user", RoleConstants.Names.StaffName),
            "director" or "admin" => ("director-user", RoleConstants.Names.DirectorName),
            "superuser" => ("super-user", RoleConstants.Names.SuperuserName),
            _ => ("director-user", RoleConstants.Names.DirectorName)
        };
    }
}