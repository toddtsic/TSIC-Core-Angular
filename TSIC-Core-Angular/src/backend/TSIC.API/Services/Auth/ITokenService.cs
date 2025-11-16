using Microsoft.AspNetCore.Identity;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.API.Services.Auth;

public interface ITokenService
{
    string GenerateMinimalJwtToken(ApplicationUser user);
    string GenerateEnrichedJwtToken(ApplicationUser user, string regId, string jobPath, string? jobLogo, string roleName);
}
