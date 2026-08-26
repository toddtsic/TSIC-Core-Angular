using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Uploads;

namespace TSIC.API.Controllers;

/// <summary>
/// Per-player medical-form PDF upload, download, and delete. Files are keyed
/// by the player's identity userId — global to the person, persistent across
/// jobs. Mirrors legacy MedForms\{userId}.pdf storage convention so existing
/// uploaded files keep working without migration.
///
/// That person-global keying is why the registration-keyed endpoints below enforce an EVENT
/// GATE as well as a job check: a file uploaded for one event is on disk for every event the
/// person registers in, so an event whose profile never collected a medical form must not be
/// able to probe for or stream one. See UploadedDocumentPolicy.
/// </summary>
[ApiController]
[Authorize]
[Route("api/files/medform")]
public class MedFormController : ControllerBase
{
    private readonly IMedFormService _medForms;
    private readonly IFamilyRepository _families;
    private readonly IRegistrationRepository _registrations;
    private readonly IJobLookupService _jobLookup;
    private readonly ILogger<MedFormController> _logger;

    public MedFormController(
        IMedFormService medForms,
        IFamilyRepository families,
        IRegistrationRepository registrations,
        IJobLookupService jobLookup,
        ILogger<MedFormController> logger)
    {
        _medForms = medForms;
        _families = families;
        _registrations = registrations;
        _jobLookup = jobLookup;
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

    /// <summary>Stream a player's med form for an admin, keyed by a registration in the admin's job.</summary>
    [HttpGet("by-registration/{registrationId:guid}")]
    [ProducesResponseType(typeof(FileStreamResult), 200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DownloadByRegistration(Guid registrationId, CancellationToken ct)
    {
        var (authorized, playerUserId) = await ResolveRegistrationAccessAsync(registrationId, ct);
        if (!authorized)
        {
            LogAccess("download(reg)", registrationId.ToString(), denied: true);
            return Forbid();
        }
        if (string.IsNullOrWhiteSpace(playerUserId)) return NotFound();

        var stream = await _medForms.ReadAsync(playerUserId, ct);
        if (stream == null)
        {
            LogAccess("download(reg)", registrationId.ToString(), denied: false, error: "NotFound");
            return NotFound();
        }

        LogAccess("download(reg)", registrationId.ToString(), denied: false);
        return File(stream, "application/pdf", $"medform-{playerUserId}.pdf");
    }

    /// <summary>Existence probe for the admin registration-keyed path — no body, just status.</summary>
    [HttpHead("by-registration/{registrationId:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> HeadByRegistration(Guid registrationId, CancellationToken ct)
    {
        var (authorized, playerUserId) = await ResolveRegistrationAccessAsync(registrationId, ct);
        if (!authorized) return Forbid();
        if (string.IsNullOrWhiteSpace(playerUserId)) return NotFound();
        return _medForms.Exists(playerUserId) ? NoContent() : NotFound();
    }

    /// <summary>
    /// Self-service authorization for the userId-keyed endpoints: the caller is the player
    /// themselves, or is in the same family group as the player. Admin roles do NOT flow through
    /// here — they use the registration-keyed endpoints, which validate the record belongs to the
    /// caller's job. Keeping admins off this (job-global) userId path is what stops it from being an
    /// unscoped back-door to any person's file.
    /// </summary>
    private async Task<bool> IsAuthorizedAsync(string playerUserId, CancellationToken ct)
    {
        var callerUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(callerUserId)) return false;

        if (string.Equals(callerUserId, playerUserId, StringComparison.OrdinalIgnoreCase))
            return true;

        return await _families.IsPlayerInFamilyAsync(callerUserId, playerUserId, ct);
    }

    /// <summary>
    /// Admin authorization for the registration-keyed endpoints, returning the resolved player
    /// userId when allowed. Only admin roles reach here. Superuser is global; Director and
    /// SuperDirector are scoped to their own job — the named registration MUST belong to it (the
    /// job check IS the fix for the prior any-admin/any-file IDOR). A missing registration and a
    /// wrong-job registration are indistinguishable (both unauthorized), so nothing leaks whether a
    /// registration exists in another job.
    /// On top of that, the registration's OWN job must actually collect medical forms
    /// (UploadedDocumentPolicy) — a check that binds Superuser too, since it is a property of
    /// the event rather than of the caller.
    /// </summary>
    private async Task<(bool Authorized, string? PlayerUserId)> ResolveRegistrationAccessAsync(
        Guid registrationId, CancellationToken ct)
    {
        // The role claim carries the role NAME (TokenService writes ClaimTypes.Role = roleName),
        // NOT the role GUID — so compare against RoleConstants.Names, not the GUID constants.
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        var isSuperuser = string.Equals(role, RoleConstants.Names.SuperuserName, StringComparison.OrdinalIgnoreCase);
        var isDirector = string.Equals(role, RoleConstants.Names.DirectorName, StringComparison.OrdinalIgnoreCase)
                      || string.Equals(role, RoleConstants.Names.SuperDirectorName, StringComparison.OrdinalIgnoreCase);

        if (!isSuperuser && !isDirector) return (false, null);

        var reg = await _registrations.GetRegistrationJobAndUserAsync(registrationId, ct);
        if (reg is null) return (false, null);

        // EVENT GATE. Applies to Superuser too - collecting med forms is a property of the JOB, not
        // of the caller's role. Files are keyed by person, not by event, so a form uploaded for one
        // event sits on disk for every event that person later registers in. Without this the control
        // renders off disk-existence alone and a director who never asked for a medical form is handed
        // one belonging to an unrelated customer. Refuse before the file is ever resolved, and return
        // the same unauthorized shape as a wrong-job hit so nothing distinguishes the two.
        if (!UploadedDocumentPolicy.CollectsMedForm(reg.Value.JobPlayerProfileMetadataJson))
            return (false, null);

        if (isSuperuser)
            return (true, reg.Value.UserId);

        var callerJobId = await User.GetJobIdFromRegistrationAsync(_jobLookup);
        if (callerJobId is null || callerJobId.Value != reg.Value.JobId)
            return (false, null);

        return (true, reg.Value.UserId);
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
