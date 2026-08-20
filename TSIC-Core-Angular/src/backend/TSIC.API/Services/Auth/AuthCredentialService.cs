using Microsoft.AspNetCore.Identity;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.API.Services.Auth;

/// <summary>
/// The one place username/password is validated, including the Development-only bypass.
/// Login, QuickLogin and ScorerLogin each grew their own inline copy of this logic; a
/// fourth copy on the mobile controller is exactly how one path ends up forgetting the
/// environment guard.
/// </summary>
public interface IAuthCredentialService
{
    Task<bool> IsPasswordValidAsync(ApplicationUser user, string password);
}

public sealed class AuthCredentialService : IAuthCredentialService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;

    public AuthCredentialService(
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        IWebHostEnvironment env)
    {
        _userManager = userManager;
        _configuration = configuration;
        _env = env;
    }

    public async Task<bool> IsPasswordValidAsync(ApplicationUser user, string password)
    {
        // The bypass is gated on the hosting environment FIRST and is never consulted
        // outside Development, regardless of what the configuration says.
        if (_env.IsDevelopment())
        {
            var allowBypass = _configuration.GetValue<bool>("DevMode:AllowPasswordBypass");
            var bypassPassword = _configuration["DevMode:BypassPassword"];
            if (allowBypass && !string.IsNullOrEmpty(bypassPassword) && password == bypassPassword)
            {
                return true;
            }
        }

        return await _userManager.CheckPasswordAsync(user, password);
    }
}
