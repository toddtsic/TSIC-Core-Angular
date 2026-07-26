using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Self-service registrant headshot upload, delete, and existence probe. Files are keyed
/// by the registrant's identity userId — global to the person, stored as {userId}.jpg in
/// the public Headshots-AllRegistrants statics folder. Headshots are public-by-GUID by
/// design, so there is deliberately NO admin path and NO job-scope guard: a registrant
/// (or a family member managing that registrant) uploads their OWN headshot, and everyone
/// reads it directly from the statics URL. Display-only surfaces (e.g. the admin search
/// fly-in) use that URL and never call this controller.
/// </summary>
[ApiController]
[Authorize]
[Route("api/files/headshot")]
public class HeadshotController : ControllerBase
{
    private readonly IHeadshotService _headshots;
    private readonly IFamilyRepository _families;
    private readonly ILogger<HeadshotController> _logger;

    public HeadshotController(
        IHeadshotService headshots,
        IFamilyRepository families,
        ILogger<HeadshotController> logger)
    {
        _headshots = headshots;
        _families = families;
        _logger = logger;
    }

    /// <summary>Upload (or replace) the registrant's headshot.</summary>
    [HttpPost("{userId}")]
    [RequestSizeLimit(6 * 1024 * 1024)] // 6 MB request cap (5 MB content + multipart overhead)
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> Upload(string userId, IFormFile file, CancellationToken ct)
    {
        if (!await IsAuthorizedAsync(userId, ct))
        {
            LogAccess("upload", userId, denied: true);
            return Forbid();
        }

        if (file == null || file.Length == 0)
            return BadRequest(new { Error = "No file provided" });

        await using var stream = file.OpenReadStream();
        var result = await _headshots.UploadAsync(userId, stream, file.Length, ct);

        if (result.Status != HeadshotUploadStatus.Ok)
        {
            LogAccess("upload", userId, denied: false, error: result.Status.ToString());
            return result.Status switch
            {
                HeadshotUploadStatus.InvalidImage => BadRequest(new { Error = "File is not a valid image (JPG, PNG, or WebP)" }),
                HeadshotUploadStatus.TooLarge => BadRequest(new { Error = "Image exceeds the 5 MB limit" }),
                HeadshotUploadStatus.InvalidUserId => BadRequest(new { Error = "Invalid user id" }),
                _ => BadRequest(new { Error = "Upload failed" }),
            };
        }

        LogAccess("upload", userId, denied: false);
        return NoContent();
    }

    /// <summary>Lightweight existence probe — no body, just status.</summary>
    [HttpHead("{userId}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Head(string userId, CancellationToken ct)
    {
        if (!await IsAuthorizedAsync(userId, ct)) return Forbid();
        return _headshots.Exists(userId) ? NoContent() : NotFound();
    }

    /// <summary>Delete the registrant's headshot (reverts display to the anonymous placeholder).</summary>
    [HttpDelete("{userId}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(string userId, CancellationToken ct)
    {
        if (!await IsAuthorizedAsync(userId, ct))
        {
            LogAccess("delete", userId, denied: true);
            return Forbid();
        }

        var deleted = await _headshots.DeleteAsync(userId, ct);
        LogAccess("delete", userId, denied: false, error: deleted ? null : "NotFound");
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>
    /// Self-service authorization: the caller is the registrant themselves, or is in the same
    /// family group as the registrant (a parent managing a player). Admin roles have no special
    /// path here by design — headshots are public-by-GUID, so there is nothing to gate for reads,
    /// and writes are strictly the registrant's own.
    /// </summary>
    private async Task<bool> IsAuthorizedAsync(string userId, CancellationToken ct)
    {
        var callerUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(callerUserId)) return false;

        if (string.Equals(callerUserId, userId, StringComparison.OrdinalIgnoreCase))
            return true;

        return await _families.IsPlayerInFamilyAsync(callerUserId, userId, ct);
    }

    private void LogAccess(string action, string userId, bool denied, string? error = null)
    {
        var actorUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (denied)
        {
            _logger.LogWarning(
                "Headshot {Action} DENIED: actor {ActorUserId} (role {Role}) -> user {UserId} from {Ip}.",
                action, actorUserId, role, userId, ip);
        }
        else if (error != null)
        {
            _logger.LogInformation(
                "Headshot {Action} {Error}: actor {ActorUserId} (role {Role}) -> user {UserId} from {Ip}.",
                action, error, actorUserId, role, userId, ip);
        }
        else
        {
            _logger.LogInformation(
                "Headshot {Action} OK: actor {ActorUserId} (role {Role}) -> user {UserId} from {Ip}.",
                action, actorUserId, role, userId, ip);
        }
    }
}
