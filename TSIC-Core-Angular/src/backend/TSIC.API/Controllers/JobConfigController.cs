using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.JobConfig;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using static TSIC.Domain.Constants.BrandingImageConventions;

namespace TSIC.API.Controllers;

/// <summary>
/// Job Configuration Editor — AdminOnly (Director, SuperDirector, SuperUser).
/// Per-tab save endpoints with role-based field filtering in the service layer.
/// </summary>
[ApiController]
[Route("api/job-config")]
[Authorize(Policy = "AdminOnly")]
public class JobConfigController : ControllerBase
{
    private readonly IJobConfigService _configService;
    private readonly IJobLookupService _jobLookupService;
    private readonly IJobImageService _imageService;

    public JobConfigController(
        IJobConfigService configService,
        IJobLookupService jobLookupService,
        IJobImageService imageService)
    {
        _configService = configService;
        _jobLookupService = jobLookupService;
        _imageService = imageService;
    }

    // ── Helpers ─────────────────────────────────────────────

    private bool IsSuperUser =>
        User.IsInRole(RoleConstants.Names.SuperuserName);

    private async Task<Guid?> GetJobIdAsync() =>
        await User.GetJobIdFromRegistrationAsync(_jobLookupService);

    // ── Read ────────────────────────────────────────────────

    /// <summary>
    /// Get full job configuration (all 8 categories).
    /// Super-only fields are null for non-SuperUser callers.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<JobConfigFullDto>> GetConfig(CancellationToken ct)
    {
        var jobId = await GetJobIdAsync();
        if (jobId is null)
            return NotFound(new { message = "Job not found for current user." });

        var result = await _configService.GetFullConfigAsync(jobId.Value, IsSuperUser, ct);
        return Ok(result);
    }

    /// <summary>
    /// Get reference/lookup data for editor dropdowns (JobTypes, Sports, Customers, BillingTypes, ChargeTypes).
    /// </summary>
    [HttpGet("reference-data")]
    public async Task<ActionResult<JobConfigReferenceDataDto>> GetReferenceData(CancellationToken ct)
    {
        var result = await _configService.GetReferenceDataAsync(ct);
        return Ok(result);
    }

    // ── Per-Tab Updates ─────────────────────────────────────

    /// <summary>Update General tab fields.</summary>
    [HttpPut("general")]
    public async Task<IActionResult> UpdateGeneral(
        [FromBody] UpdateJobConfigGeneralRequest request, CancellationToken ct)
    {
        var jobId = await GetJobIdAsync();
        if (jobId is null)
            return NotFound(new { message = "Job not found for current user." });

        await _configService.UpdateGeneralAsync(jobId.Value, request, IsSuperUser, ct);
        return NoContent();
    }

    /// <summary>Update Payment &amp; Billing tab fields.</summary>
    [HttpPut("payment")]
    public async Task<IActionResult> UpdatePayment(
        [FromBody] UpdateJobConfigPaymentRequest request, CancellationToken ct)
    {
        var jobId = await GetJobIdAsync();
        if (jobId is null)
            return NotFound(new { message = "Job not found for current user." });

        await _configService.UpdatePaymentAsync(jobId.Value, request, IsSuperUser, ct);
        return NoContent();
    }

    /// <summary>Update Communications tab fields.</summary>
    [HttpPut("communications")]
    public async Task<IActionResult> UpdateCommunications(
        [FromBody] UpdateJobConfigCommunicationsRequest request, CancellationToken ct)
    {
        var jobId = await GetJobIdAsync();
        if (jobId is null)
            return NotFound(new { message = "Job not found for current user." });

        await _configService.UpdateCommunicationsAsync(jobId.Value, request, ct);
        return NoContent();
    }

    /// <summary>Update Player Registration tab fields.</summary>
    [HttpPut("player")]
    public async Task<IActionResult> UpdatePlayer(
        [FromBody] UpdateJobConfigPlayerRequest request, CancellationToken ct)
    {
        var jobId = await GetJobIdAsync();
        if (jobId is null)
            return NotFound(new { message = "Job not found for current user." });

        await _configService.UpdatePlayerAsync(jobId.Value, request, IsSuperUser, ct);
        return NoContent();
    }

    /// <summary>Update Teams &amp; Club Reps tab fields.</summary>
    [HttpPut("teams")]
    public async Task<IActionResult> UpdateTeams(
        [FromBody] UpdateJobConfigTeamsRequest request, CancellationToken ct)
    {
        var jobId = await GetJobIdAsync();
        if (jobId is null)
            return NotFound(new { message = "Job not found for current user." });

        await _configService.UpdateTeamsAsync(jobId.Value, request, IsSuperUser, ct);
        return NoContent();
    }

    /// <summary>Update Coaches &amp; Staff tab fields.</summary>
    [HttpPut("coaches")]
    public async Task<IActionResult> UpdateCoaches(
        [FromBody] UpdateJobConfigCoachesRequest request, CancellationToken ct)
    {
        var jobId = await GetJobIdAsync();
        if (jobId is null)
            return NotFound(new { message = "Job not found for current user." });

        await _configService.UpdateCoachesAsync(jobId.Value, request, ct);
        return NoContent();
    }

    /// <summary>Update Scheduling tab fields.</summary>
    [HttpPut("scheduling")]
    public async Task<IActionResult> UpdateScheduling(
        [FromBody] UpdateJobConfigSchedulingRequest request, CancellationToken ct)
    {
        var jobId = await GetJobIdAsync();
        if (jobId is null)
            return NotFound(new { message = "Job not found for current user." });

        await _configService.UpdateSchedulingAsync(jobId.Value, request, ct);
        return NoContent();
    }

    /// <summary>Update Mobile &amp; Store tab fields.</summary>
    [HttpPut("mobile-store")]
    public async Task<IActionResult> UpdateMobileStore(
        [FromBody] UpdateJobConfigMobileStoreRequest request, CancellationToken ct)
    {
        var jobId = await GetJobIdAsync();
        if (jobId is null)
            return NotFound(new { message = "Job not found for current user." });

        await _configService.UpdateMobileStoreAsync(jobId.Value, request, IsSuperUser, ct);
        return NoContent();
    }

    // ── Branding ──────────────────────────────────────────────

    /// <summary>Update Branding tab fields (toggle + overlay text).</summary>
    [HttpPut("branding")]
    public async Task<IActionResult> UpdateBranding(
        [FromBody] UpdateJobConfigBrandingRequest request, CancellationToken ct)
    {
        var jobId = await GetJobIdAsync();
        if (jobId is null)
            return NotFound(new { message = "Job not found for current user." });

        await _configService.UpdateBrandingAsync(jobId.Value, request, ct);
        return NoContent();
    }

    /// <summary>Upload a branding image (banner-bg, banner-overlay, or logo).</summary>
    [HttpPost("branding/images/{conventionName}")]
    [RequestSizeLimit(5_242_880)]
    public async Task<ActionResult<JobImageUploadResultDto>> UploadBrandingImage(
        string conventionName, IFormFile file, CancellationToken ct)
    {
        if (!ImageSpecs.ContainsKey(conventionName))
            return BadRequest(new { message = $"Unknown convention name: {conventionName}" });

        var jobId = await GetJobIdAsync();
        if (jobId is null)
            return NotFound(new { message = "Job not found for current user." });

        var spec = ImageSpecs[conventionName];

        await using var stream = file.OpenReadStream();
        var fileName = await _imageService.SaveJobImageAsync(
            stream, file.FileName, jobId.Value, conventionName, spec.MaxWidth, spec.Quality, ct);

        await _configService.UpdateBrandingImageFieldAsync(jobId.Value, conventionName, fileName, ct);

        return Ok(new JobImageUploadResultDto
        {
            FileName = fileName,
            Url = $"{TsicConstants.BaseUrlStatics}BannerFiles/{fileName}",
        });
    }

    /// <summary>Delete a branding image.</summary>
    [HttpDelete("branding/images/{conventionName}")]
    public async Task<IActionResult> DeleteBrandingImage(
        string conventionName, CancellationToken ct)
    {
        if (!ImageSpecs.ContainsKey(conventionName))
            return BadRequest(new { message = $"Unknown convention name: {conventionName}" });

        var jobId = await GetJobIdAsync();
        if (jobId is null)
            return NotFound(new { message = "Job not found for current user." });

        await _imageService.DeleteJobImageAsync(jobId.Value, conventionName, ct);
        await _configService.UpdateBrandingImageFieldAsync(jobId.Value, conventionName, null, ct);

        return NoContent();
    }

    // ── Admin Charges (SuperUser only) ──────────────────────

    /// <summary>Add an admin charge to the job.</summary>
    [HttpPost("admin-charges")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<ActionResult<JobAdminChargeDto>> AddAdminCharge(
        [FromBody] CreateAdminChargeRequest request, CancellationToken ct)
    {
        var jobId = await GetJobIdAsync();
        if (jobId is null)
            return NotFound(new { message = "Job not found for current user." });

        var result = await _configService.AddAdminChargeAsync(jobId.Value, request, ct);
        return Ok(result);
    }

    /// <summary>Delete an admin charge from the job.</summary>
    [HttpDelete("admin-charges/{chargeId:int}")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<IActionResult> DeleteAdminCharge(int chargeId, CancellationToken ct)
    {
        var jobId = await GetJobIdAsync();
        if (jobId is null)
            return NotFound(new { message = "Job not found for current user." });

        await _configService.DeleteAdminChargeAsync(jobId.Value, chargeId, ct);
        return NoContent();
    }
}
