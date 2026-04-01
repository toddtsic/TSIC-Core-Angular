using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Controllers;

/// <summary>
/// Dev-only registration utilities. Returns 404 in production.
/// Allows testers to delete a registration so they can re-register the same player.
/// </summary>
[ApiController]
[Route("api/dev/registration")]
[Authorize(Policy = "SuperUserOnly")]
public class DevRegistrationController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly IRegistrationRepository _regRepo;
    private readonly IRegistrationAccountingRepository _acctRepo;
    private readonly ILogger<DevRegistrationController> _logger;

    public DevRegistrationController(
        IWebHostEnvironment env,
        IRegistrationRepository regRepo,
        IRegistrationAccountingRepository acctRepo,
        ILogger<DevRegistrationController> logger)
    {
        _env = env;
        _regRepo = regRepo;
        _acctRepo = acctRepo;
        _logger = logger;
    }

    /// <summary>
    /// DELETE /api/dev/registration/{registrationId}
    /// Deletes accounting records then the registration itself.
    /// Dev/test only — returns 404 in production.
    /// </summary>
    [HttpDelete("{registrationId:guid}")]
    public async Task<IActionResult> DeleteRegistration(Guid registrationId, CancellationToken ct)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        var reg = await _regRepo.GetByIdAsync(registrationId, ct);
        if (reg == null)
            return NotFound(new { message = "Registration not found" });

        var actor = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        _logger.LogWarning(
            "DEV DELETE — Registration {RegistrationId} (User={UserId}, Job={JobId}) by {Actor}",
            registrationId, reg.UserId, reg.JobId, actor);

        // 1. Delete accounting records (payments, adjustments)
        await _acctRepo.DeleteByRegistrationIdAsync(registrationId, ct);

        // 2. Delete the registration
        _regRepo.Remove(reg);
        await _regRepo.SaveChangesAsync(ct);

        _logger.LogWarning("DEV DELETE — Registration {RegistrationId} deleted successfully", registrationId);
        return Ok(new { message = "Registration deleted", registrationId });
    }
}
