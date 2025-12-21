using System;
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
    public async Task<IActionResult> Get([FromQuery] Guid jobId, [FromQuery] Guid familyUserId, CancellationToken ct)
    {
        _logger.LogInformation("[Confirmation] GET invoked jobId={JobId} familyUserId={FamilyUserId}", jobId, familyUserId);
        if (jobId == Guid.Empty || familyUserId == Guid.Empty)
        {
            return BadRequest(new { message = "Invalid parameters" });
        }

        // Authorization: ensure caller matches familyUserId.
        var claimId = User?.FindFirst("sub")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(claimId) || !claimId.Equals(familyUserId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Confirmation access denied for familyUserId={FamilyUserId} caller={Caller}", familyUserId, claimId);
            return Forbid();
        }

        var dto = await _service.BuildAsync(jobId, familyUserId.ToString(), ct);
        return Ok(dto);
    }

    // HEAD endpoint (some clients/browsers may probe; avoids 405)
    [HttpHead("confirmation")]
    [Authorize]
    public IActionResult Head([FromQuery] Guid jobId, [FromQuery] Guid familyUserId)
    {
        if (jobId == Guid.Empty || familyUserId == Guid.Empty) return BadRequest();
        var claimId = User?.FindFirst("sub")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(claimId) || !claimId.Equals(familyUserId.ToString(), StringComparison.OrdinalIgnoreCase)) return Forbid();
        return Ok();
    }

    [HttpPost("confirmation/resend")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Resend([FromQuery] Guid jobId, [FromQuery] Guid familyUserId, CancellationToken ct)
    {
        _logger.LogInformation("[Confirmation] RESEND invoked jobId={JobId} familyUserId={FamilyUserId}", jobId, familyUserId);
        if (jobId == Guid.Empty || familyUserId == Guid.Empty)
        {
            return BadRequest(new { message = "Invalid parameters" });
        }

        var claimId = User?.FindFirst("sub")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(claimId) || !claimId.Equals(familyUserId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Confirmation resend denied for familyUserId={FamilyUserId} caller={Caller}", familyUserId, claimId);
            return Forbid();
        }

        // Build distinct recipient list: player emails for this job + mom/dad from Families
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fam = await _familyRepo.GetByFamilyUserIdAsync(familyUserId.ToString());
        if (fam != null)
        {
            if (!string.IsNullOrWhiteSpace(fam.MomEmail)) recipients.Add(fam.MomEmail.Trim());
            if (!string.IsNullOrWhiteSpace(fam.DadEmail)) recipients.Add(fam.DadEmail.Trim());
        }
        var playerRegs = await _familyRepo.GetFamilyRegistrationsForJobAsync(jobId, familyUserId.ToString(), ct);
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

        var (subject, html) = await _service.BuildEmailAsync(jobId, familyUserId.ToString(), ct);
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
