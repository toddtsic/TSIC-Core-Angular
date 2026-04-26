using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Controllers;

/// <summary>
/// Per-player medical-form PDF upload, download, and delete. Files are keyed
/// by the player's identity userId — global to the person, persistent across
/// jobs. Mirrors legacy MedForms\{userId}.pdf storage convention so existing
/// uploaded files keep working without migration.
/// </summary>
[ApiController]
[Authorize]
[Route("api/files/medform")]
public class MedFormController : ControllerBase
{
    private readonly IMedFormService _medForms;
    private readonly IFamilyRepository _families;
    private readonly ILogger<MedFormController> _logger;

    public MedFormController(
        IMedFormService medForms,
        IFamilyRepository families,
        ILogger<MedFormController> logger)
    {
        _medForms = medForms;
        _families = families;
        _logger = logger;
    }

    /// <summary>Upload a med form PDF for the named player.</summary>
    [HttpPost("{playerUserId}")]
    [RequestSizeLimit(11 * 1024 * 1024)] // 11 MB request cap (10 MB content + multipart overhead)
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> Upload(
        string playerUserId,
        IFormFile file,
        CancellationToken ct)
    {
        if (!await IsAuthorizedAsync(playerUserId, ct))
        {
            LogAccess("upload", playerUserId, denied: true);
            return Forbid();
        }

        if (file == null || file.Length == 0)
            return BadRequest(new { Error = "No file provided" });

        await using var stream = file.OpenReadStream();
        var result = await _medForms.UploadAsync(playerUserId, stream, file.Length, ct);

        if (result.Status != MedFormUploadStatus.Ok)
        {
            LogAccess("upload", playerUserId, denied: false, error: result.Status.ToString());
            return result.Status switch
            {
                MedFormUploadStatus.InvalidPdf => BadRequest(new { Error = "File is not a valid PDF" }),
                MedFormUploadStatus.TooLarge => BadRequest(new { Error = "File exceeds 10 MB limit" }),
                MedFormUploadStatus.InvalidPlayerUserId => BadRequest(new { Error = "Invalid player id" }),
                _ => BadRequest(new { Error = "Upload failed" }),
            };
        }

        LogAccess("upload", playerUserId, denied: false);
        return NoContent();
    }

    /// <summary>Stream the player's med form PDF.</summary>
    [HttpGet("{playerUserId}")]
    [ProducesResponseType(typeof(FileStreamResult), 200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Download(string playerUserId, CancellationToken ct)
    {
        if (!await IsAuthorizedAsync(playerUserId, ct))
        {
            LogAccess("download", playerUserId, denied: true);
            return Forbid();
        }

        var stream = await _medForms.ReadAsync(playerUserId, ct);
        if (stream == null)
        {
            LogAccess("download", playerUserId, denied: false, error: "NotFound");
            return NotFound();
        }

        LogAccess("download", playerUserId, denied: false);
        return File(stream, "application/pdf", $"medform-{playerUserId}.pdf");
    }

    /// <summary>Lightweight existence probe — no body, just status.</summary>
    [HttpHead("{playerUserId}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Head(string playerUserId, CancellationToken ct)
    {
        if (!await IsAuthorizedAsync(playerUserId, ct)) return Forbid();
        return _medForms.Exists(playerUserId) ? NoContent() : NotFound();
    }

    /// <summary>Delete the player's med form.</summary>
    [HttpDelete("{playerUserId}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(string playerUserId, CancellationToken ct)
    {
        if (!await IsAuthorizedAsync(playerUserId, ct))
        {
            LogAccess("delete", playerUserId, denied: true);
            return Forbid();
        }

        var deleted = await _medForms.DeleteAsync(playerUserId, ct);
        LogAccess("delete", playerUserId, denied: false, error: deleted ? null : "NotFound");
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>
    /// Authorized = admin role OR caller is the player themselves OR caller is
    /// in the same family group as the player.
    /// </summary>
    private async Task<bool> IsAuthorizedAsync(string playerUserId, CancellationToken ct)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (role is RoleConstants.Director
                 or RoleConstants.SuperDirector
                 or RoleConstants.Superuser)
        {
            return true;
        }

        var callerUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(callerUserId)) return false;

        return await _families.IsPlayerInFamilyAsync(callerUserId, playerUserId, ct);
    }

    private void LogAccess(string action, string playerUserId, bool denied, string? error = null)
    {
        var actorUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (denied)
        {
            _logger.LogWarning(
                "MedForm {Action} DENIED: actor {ActorUserId} (role {Role}) -> player {PlayerUserId} from {Ip}.",
                action, actorUserId, role, playerUserId, ip);
        }
        else if (error != null)
        {
            _logger.LogInformation(
                "MedForm {Action} {Error}: actor {ActorUserId} (role {Role}) -> player {PlayerUserId} from {Ip}.",
                action, error, actorUserId, role, playerUserId, ip);
        }
        else
        {
            _logger.LogInformation(
                "MedForm {Action} OK: actor {ActorUserId} (role {Role}) -> player {PlayerUserId} from {Ip}.",
                action, actorUserId, role, playerUserId, ip);
        }
    }
}
