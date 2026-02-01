using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.API.Services.Auth;

public sealed class TokenService : ITokenService
{
    private readonly IConfiguration _configuration;

    public TokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateMinimalJwtToken(ApplicationUser user)
    {
        var (issuer, audience, secretKey, expirationMinutes) = GetJwtSettings();

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id), // user ID in sub
            new Claim("username", user.UserName ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        };

        return WriteToken(claims, issuer, audience, secretKey, expirationMinutes);
    }

    public string GenerateEnrichedJwtToken(ApplicationUser user, string regId, string jobPath, string? jobLogo, string roleName)
    {
        var (issuer, audience, secretKey, expirationMinutes) = GetJwtSettings();

        var claimsList = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim("username", user.UserName ?? string.Empty),
            new Claim("regId", regId),
            new Claim("jobPath", jobPath),
            new Claim(ClaimTypes.Role, roleName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        };

        if (!string.IsNullOrWhiteSpace(jobLogo))
            claimsList.Add(new Claim("jobLogo", jobLogo));

        return WriteToken(claimsList, issuer, audience, secretKey, expirationMinutes);
    }

    public string GenerateJobScopedToken(ApplicationUser user, string jobPath, string? jobLogo, string roleName)
    {
        var (issuer, audience, secretKey, expirationMinutes) = GetJwtSettings();

        var claimsList = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim("username", user.UserName ?? string.Empty),
            new Claim("jobPath", jobPath),
            new Claim(ClaimTypes.Role, roleName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        };

        if (!string.IsNullOrWhiteSpace(jobLogo))
            claimsList.Add(new Claim("jobLogo", jobLogo));

        return WriteToken(claimsList, issuer, audience, secretKey, expirationMinutes);
    }

    private (string issuer, string audience, string secretKey, int expirationMinutes) GetJwtSettings()
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");
        var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey not configured");
        var issuer = jwtSettings["Issuer"] ?? "TSIC.API";
        var audience = jwtSettings["Audience"] ?? "TSIC.Client";
        var expirationMinutes = int.Parse(jwtSettings["ExpirationMinutes"] ?? "60");
        return (issuer, audience, secretKey, expirationMinutes);
    }

    private static string WriteToken(IEnumerable<Claim> claims, string issuer, string audience, string secretKey, int expirationMinutes)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: credentials
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
