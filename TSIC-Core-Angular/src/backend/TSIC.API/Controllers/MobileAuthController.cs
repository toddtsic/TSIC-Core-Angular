using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Services.Auth;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Mobile;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.API.Controllers;

/// <summary>
/// Auth for the TSIC-Teams mobile app.
///
/// Mobile is not the web app with fewer roles. The web flow is authenticate, pick a ROLE,
/// pick a registration within it — role is how the web routes between many surfaces. A
/// parent opening a phone does not think "log in as Player", they think "Emma team". So
/// mobile drops the role layer and offers the thing people actually pick: a kid and a team.
///
/// That is a different response shape and a different number of round trips, which is why
/// it is its own controller rather than a flag on the shared one. A request-supplied
/// "I am the mobile app" parameter would be worse than useless here: it is not an
/// authentication signal, and sooner or later something would branch AUTHORIZATION on it.
///
/// Password validation, token minting and refresh-token issuing are shared services. Only
/// the shape and the flow are mobile-specific. auth/refresh and auth/revoke are token
/// mechanics, identical for every client — mobile calls those endpoints unchanged.
/// </summary>
[ApiController]
[Route("api/mobile/auth")]
public class MobileAuthController : ControllerBase
{
    private readonly IMobileAuthService _mobileAuth;
    private readonly IRegistrationSelectionService _selection;
    private readonly UserManager<ApplicationUser> _userManager;

    public MobileAuthController(
        IMobileAuthService mobileAuth,
        IRegistrationSelectionService selection,
        UserManager<ApplicationUser> userManager)
    {
        _mobileAuth = mobileAuth;
        _selection = selection;
        _userManager = userManager;
    }

    /// <summary>
    /// Authenticate and return everything the picker needs in one round trip.
    ///
    /// 401 on bad credentials, and nothing else. Both arrays may come back empty — a
    /// Referee, Store Admin or Club Rep authenticates successfully and has nothing in this
    /// app. That is a 200, not a 403: excluding them at login would make the failure look
    /// like broken credentials, and returning empty arrays lets the app say something true.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(MobileLoginResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Login([FromBody] MobileLoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { Error = "Username and password are required." });
        }

        var result = await _mobileAuth.LoginAsync(request.Username, request.Password, ct);
        if (result == null)
        {
            return Unauthorized(new { Error = "Invalid username or password" });
        }

        return Ok(result);
    }

    /// <summary>
    /// Teams owned by an ownership registration. Called only after the user picks one; the
    /// login response carries a COUNT, never the list, because a superuser across 50 jobs
    /// would otherwise inline roughly 10,000 teams into the login path.
    /// </summary>
    [Authorize]
    [HttpGet("ownerships/{regId:guid}/teams")]
    [ProducesResponseType(typeof(List<MobileOwnershipTeamDto>), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetOwnershipTeams(Guid regId, CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new { Error = "Invalid token" });
        }

        var result = await _mobileAuth.GetOwnershipTeamsAsync(userId, regId, ct);

        return result.Outcome switch
        {
            OwnershipTeamsOutcome.Ok => Ok(result.Teams),
            OwnershipTeamsOutcome.NotAnOwnership => BadRequest(
                new { Error = "That registration is a roster seat and has no teams to choose between." }),
            _ => StatusCode(StatusCodes.Status403Forbidden,
                new { Error = "That registration does not belong to this account." })
        };
    }

    /// <summary>
    /// Exchange a regId for the enriched token. Identical in behaviour to the web
    /// auth/select-registration and calls the SAME service — present here only so the whole
    /// mobile flow reads in one file. The phase-1 refresh token stays valid and is reused;
    /// no second refresh token is issued.
    /// </summary>
    [Authorize]
    [HttpPost("select-registration")]
    [ProducesResponseType(typeof(AuthTokenResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> SelectRegistration([FromBody] MobileSelectRegistrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RegId))
        {
            return BadRequest(new { Error = "RegId is required" });
        }

        var userId = GetUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new { Error = "Invalid token" });
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Unauthorized(new { Error = "Invalid user" });
        }

        var result = await _selection.SelectAsync(user, request.RegId);
        if (!result.Succeeded)
        {
            return BadRequest(new { Error = "Selected registration is not available for this user" });
        }

        return Ok(new AuthTokenResponse
        {
            AccessToken = result.AccessToken!,
            ExpiresIn = result.ExpiresInSeconds
        });
    }

    /// <summary>
    /// sub carries the user ID, not the username — ASP.NET remaps it to NameIdentifier.
    /// </summary>
    private string? GetUserId() =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
}
