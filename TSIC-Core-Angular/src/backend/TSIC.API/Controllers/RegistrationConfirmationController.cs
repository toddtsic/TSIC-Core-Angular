using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Services;
using TSIC.API.Dtos;
using TSIC.API.Services.Email;
using TSIC.Infrastructure.Data.SqlDbContext;
using MimeKit;
using Microsoft.EntityFrameworkCore;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/registration")] // base route; action supplies segment
public sealed class RegistrationConfirmationController : ControllerBase
{
    private readonly IPlayerRegConfirmationService _service;
    private readonly ILogger<RegistrationConfirmationController> _logger;
    private readonly IEmailService _email;
    private readonly SqlDbContext _db;

    public RegistrationConfirmationController(
        IPlayerRegConfirmationService service,
        ILogger<RegistrationConfirmationController> logger,
        IEmailService email,
        SqlDbContext db)
    {
        _service = service;
        _logger = logger;
        _email = email;
        _db = db;
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
        var fam = await _db.Families.AsNoTracking().FirstOrDefaultAsync(f => f.FamilyUserId == familyUserId.ToString(), ct);
        if (!string.IsNullOrWhiteSpace(fam?.MomEmail)) recipients.Add(fam!.MomEmail!.Trim());
        if (!string.IsNullOrWhiteSpace(fam?.DadEmail)) recipients.Add(fam!.DadEmail!.Trim());
        var playerRegs = await _db.Registrations.AsNoTracking()
            .Include(r => r.User)
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId.ToString())
            .ToListAsync(ct);
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

        var message = new MimeMessage();
        foreach (var addr in toList)
        {
            message.To.Add(MailboxAddress.Parse(addr));
        }
        message.Subject = string.IsNullOrWhiteSpace(subject) ? "Registration Confirmation" : subject;
        var builder = new BodyBuilder { HtmlBody = html };
        message.Body = builder.ToMessageBody();

        var ok = await _email.SendAsync(message, sendInDevelopment: false, cancellationToken: ct);
        if (!ok) return StatusCode(502, new { message = "Email send failed" });
        return Ok(new { sent = true });
    }
}
