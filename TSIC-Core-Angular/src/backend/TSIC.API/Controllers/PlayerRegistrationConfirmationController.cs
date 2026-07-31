using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.Contracts.Services;
using TSIC.Contracts.Repositories;
using TSIC.API.Services.Players;
using TSIC.API.Services.Teams;
using TSIC.API.Services.Families;
using TSIC.API.Services.Clubs;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Metadata;
using TSIC.API.Services.Shared;
using TSIC.API.Services.Shared.Email;
using TSIC.API.Services.Shared.VerticalInsure;

using TSIC.API.Services.Auth;
using TSIC.API.Services.Shared.UsLax;
using TSIC.Contracts.Dtos;
using TSIC.Domain.Constants;


namespace TSIC.API.Controllers;

[ApiController]
[Route("api/player-registration")] // base route; action supplies segment
public sealed class PlayerRegistrationConfirmationController : ControllerBase
{
    private readonly IPlayerRegConfirmationService _service;
    private readonly ILogger<PlayerRegistrationConfirmationController> _logger;
    private readonly IEmailTestSendService _testSend;
    private readonly IHostEnvironment _env;

    public PlayerRegistrationConfirmationController(
        IPlayerRegConfirmationService service,
        ILogger<PlayerRegistrationConfirmationController> logger,
        IEmailTestSendService testSend,
        IHostEnvironment env)
    {
        _service = service;
        _logger = logger;
        _testSend = testSend;
        _env = env;
    }

    /// <summary>
    /// Sandbox-only: renders THIS family's confirmation and delivers it to a single test inbox
    /// instead of the family, so the content and formatting can be checked without emailing real
    /// people. Same render as the live resend below (BuildEmailAsync), so what arrives is what a
    /// family would get. Refused in Production — and refused again inside EmailTestSendService.
    /// </summary>
    [HttpPost("confirmation/test-send")]
    [Authorize]
    [ProducesResponseType(typeof(EmailTestSendResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<EmailTestSendResponse>> TestSend(
        [FromBody] ConfirmationTestSendRequest request, CancellationToken ct)
    {
        if (_env.IsLiveProduction())
            return BadRequest(new { message = "Test sends are not permitted in Production." });

        var jobPath = User.FindFirstValue("jobPath");
        if (string.IsNullOrWhiteSpace(jobPath))
            return BadRequest(new { message = "jobPath claim is required" });

        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(familyUserId)) return Unauthorized();

        var (subject, html) = await _service.BuildEmailAsync(jobPath, familyUserId, ct);
        if (string.IsNullOrWhiteSpace(html))
            return BadRequest(new { message = "No confirmation content available to render" });

        var result = await _testSend.SendRenderedAsync(
            string.IsNullOrWhiteSpace(subject) ? "Registration Confirmation" : subject,
            html, "this family", request.TestRecipient, ct);
        return Ok(result);
    }

    [HttpGet("confirmation")]
    [Authorize]
    [ProducesResponseType(typeof(PlayerRegConfirmationDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        // Require jobPath from JWT token claim (job-scoped token)
        var jobPath = User.FindFirstValue("jobPath");
        _logger.LogInformation("[Confirmation] GET invoked jobPath={JobPath}", jobPath);
        if (string.IsNullOrWhiteSpace(jobPath))
        {
            return BadRequest(new { message = "jobPath claim is required. Please refresh your login." });
        }

        // ASP.NET Core maps JWT 'sub' claim to ClaimTypes.NameIdentifier
        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(familyUserId))
        {
            _logger.LogWarning("Confirmation access denied: user identity not found");
            return Unauthorized();
        }

        var dto = await _service.BuildAsync(jobPath, familyUserId, ct);
        return Ok(dto);
    }

    // HEAD endpoint (some clients/browsers may probe; avoids 405)
    [HttpHead("confirmation")]
    [Authorize]
    public IActionResult Head()
    {
        var jobPath = User.FindFirstValue("jobPath");
        if (string.IsNullOrWhiteSpace(jobPath)) return BadRequest();
        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(familyUserId)) return Unauthorized();
        return Ok();
    }

    [HttpPost("confirmation/resend")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Resend(CancellationToken ct)
    {
        var jobPath = User.FindFirstValue("jobPath");
        _logger.LogInformation("[Confirmation] RESEND invoked jobPath={JobPath}", jobPath);
        if (string.IsNullOrWhiteSpace(jobPath))
        {
            return BadRequest(new { message = "jobPath claim is required" });
        }

        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(familyUserId))
        {
            _logger.LogWarning("Confirmation resend denied: user identity not found");
            return Unauthorized();
        }

        // Send lives in the service — the same redelivery chokepoint the admin fly-in resend uses
        // (Reply-To applied, CC/BCC deliberately not: the copies audience got the original).
        var result = await _service.SendConfirmationAsync(jobPath, familyUserId, ct);
        if (result.Sent) return Ok(new { sent = true });

        return result.Failure switch
        {
            PlayerConfirmationSendFailure.NoRecipients =>
                BadRequest(new { message = "No valid recipient emails found (player/mom/dad)" }),
            PlayerConfirmationSendFailure.SendFailed =>
                StatusCode(502, new { message = "Email send failed" }),
            _ => BadRequest(new { message = "No confirmation content available to send" }),
        };
    }
}
