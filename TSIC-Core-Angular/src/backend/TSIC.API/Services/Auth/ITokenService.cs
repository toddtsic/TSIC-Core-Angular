using Microsoft.AspNetCore.Identity;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.API.Services.Auth;

public interface ITokenService
{
    string GenerateMinimalJwtToken(ApplicationUser user);
    /// <param name="expirationMinutesOverride">
    /// Token lifetime in minutes. Null uses JwtSettings:ExpirationMinutes (60). Supplied only by
    /// mobile scorer login, whose session has to outlive a tournament day — see JwtSettings:ScorerExpirationMinutes.
    /// </param>
    string GenerateEnrichedJwtToken(ApplicationUser user, string regId, string jobPath, string? jobLogo, string roleName, int? expirationMinutesOverride = null);
    string GenerateJobScopedToken(ApplicationUser user, string jobPath, string? jobLogo, string roleName);
}
