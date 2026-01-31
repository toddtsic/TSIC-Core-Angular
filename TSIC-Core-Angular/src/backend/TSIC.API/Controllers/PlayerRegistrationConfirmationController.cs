using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Services;
using TSIC.Contracts.Repositories;
using TSIC.API.Services.Players;
using TSIC.API.Services.Teams;
using TSIC.API.Services.Families;
using TSIC.API.Services.Clubs;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Metadata;
using TSIC.API.Services.Shared;
using TSIC.API.Services.Shared.VerticalInsure;

using TSIC.API.Services.Auth;
using TSIC.API.Services.Shared.UsLax;
using TSIC.Contracts.Dtos;


namespace TSIC.API.Controllers;

[ApiController]
[Route("api/player-registration")] // base route; action supplies segment
public sealed class PlayerRegistrationConfirmationController : ControllerBase
{
    private readonly IPlayerRegConfirmationService _service;
    private readonly ILogger<PlayerRegistrationConfirmationController> _logger;
    private readonly IEmailService _email;
    private readonly IFamilyRepository _familyRepo;

    public PlayerRegistrationConfirmationController(
        IPlayerRegConfirmationService service,
        ILogger<PlayerRegistrationConfirmationController> logger,
        IEmailService email,
        IFamilyRepository familyRepo)
    {
        _service = service;
        _logger = logger;
        _email = email;
        _familyRepo = familyRepo;
    }

    [HttpGet("confirmation")]
    [Authorize]
    [ProducesResponseType(typeof(PlayerRegConfirmationDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Get([FromQuery] string? jobPath, CancellationToken ct)
    {
        // Accept jobPath from query parameter (family users) or JWT claim (Phase 2 users)
        var effectiveJobPath = jobPath ?? User.FindFirstValue("jobPath");
        _logger.LogInformation("[Confirmation] GET invoked jobPath={JobPath}", effectiveJobPath);
        if (string.IsNullOrWhiteSpace(effectiveJobPath))
        {
            return BadRequest(new { message = "jobPath query parameter or claim is required" });
        }

        // ASP.NET Core maps JWT 'sub' claim to ClaimTypes.NameIdentifier
        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(familyUserId))
        {
            _logger.LogWarning("Confirmation access denied: user identity not found");
            return Unauthorized();
        }

        var dto = await _service.BuildAsync(effectiveJobPath, familyUserId, ct);
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

        // Build distinct recipient list: player emails for this job + mom/dad from Families
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fam = await _familyRepo.GetByFamilyUserIdAsync(familyUserId);
        if (fam != null)
        {
            if (!string.IsNullOrWhiteSpace(fam.MomEmail)) recipients.Add(fam.MomEmail.Trim());
            if (!string.IsNullOrWhiteSpace(fam.DadEmail)) recipients.Add(fam.DadEmail.Trim());
        }
        var playerRegs = await _familyRepo.GetFamilyRegistrationsForJobAsync(jobPath, familyUserId, ct);
        foreach (var r in playerRegs)
        {
            var e = r.User?.Email?.Trim();
            if (!string.IsNullOrWhiteSpace(e)) recipients.Add(e!);
        }
        // Normalize and filter to valid looking emails
        var toList = recipients
            .Select(x => x.Trim())
            .Where(x => x.Contains('@'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (toList.Count == 0)
        {
            return BadRequest(new { message = "No valid recipient emails found (player/mom/dad)" });
        }

        var (subject, html) = await _service.BuildEmailAsync(jobPath, familyUserId, ct);
        if (string.IsNullOrWhiteSpace(html))
        {
            return BadRequest(new { message = "No confirmation content available to send" });
        }

        var dto = new EmailMessageDto
        {
            Subject = string.IsNullOrWhiteSpace(subject) ? "Registration Confirmation" : subject,
            HtmlBody = html
        };
        dto.ToAddresses.AddRange(toList);

        var ok = await _email.SendAsync(dto, sendInDevelopment: false, cancellationToken: ct);
        if (!ok) return StatusCode(502, new { message = "Email send failed" });
        return Ok(new { sent = true });
    }
}
