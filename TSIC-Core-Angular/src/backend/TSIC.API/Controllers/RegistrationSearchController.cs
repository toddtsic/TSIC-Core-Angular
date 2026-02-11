using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/registration-search")]
[Authorize(Policy = "AdminOnly")]
public class RegistrationSearchController : ControllerBase
{
    private readonly ILogger<RegistrationSearchController> _logger;
    private readonly IRegistrationSearchService _searchService;
    private readonly IJobLookupService _jobLookupService;

    public RegistrationSearchController(
        ILogger<RegistrationSearchController> logger,
        IRegistrationSearchService searchService,
        IJobLookupService jobLookupService)
    {
        _logger = logger;
        _searchService = searchService;
        _jobLookupService = jobLookupService;
    }

    private async Task<(Guid? jobId, string? userId, ActionResult? error)> ResolveContext()
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return (null, null, BadRequest(new { message = "Registration context required" }));

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return (null, null, Unauthorized());

        return (jobId, userId, null);
    }

    [HttpPost("search")]
    public async Task<ActionResult<RegistrationSearchResponse>> Search(
        [FromBody] RegistrationSearchRequest request, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required" });

        var result = await _searchService.SearchAsync(jobId.Value, request, ct);
        return Ok(result);
    }

    [HttpGet("filter-options")]
    public async Task<ActionResult<RegistrationFilterOptionsDto>> GetFilterOptions(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required" });

        var options = await _searchService.GetFilterOptionsAsync(jobId.Value, ct);
        return Ok(options);
    }

    [HttpGet("{registrationId:guid}")]
    public async Task<ActionResult<RegistrationDetailDto>> GetRegistrationDetail(
        Guid registrationId, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required" });

        var detail = await _searchService.GetRegistrationDetailAsync(registrationId, jobId.Value, ct);
        if (detail == null)
            return NotFound();

        return Ok(detail);
    }

    [HttpPut("{registrationId:guid}/profile")]
    public async Task<ActionResult> UpdateProfile(
        Guid registrationId, [FromBody] UpdateRegistrationProfileRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        // Ensure route param matches body
        var sanitized = request with { RegistrationId = registrationId };

        try
        {
            await _searchService.UpdateRegistrationProfileAsync(jobId!.Value, userId!, sanitized, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{registrationId:guid}/family")]
    public async Task<ActionResult> UpdateFamilyContact(
        Guid registrationId, [FromBody] UpdateFamilyContactRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var sanitized = request with { RegistrationId = registrationId };

        try
        {
            await _searchService.UpdateFamilyContactAsync(jobId!.Value, userId!, sanitized, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{registrationId:guid}/demographics")]
    public async Task<ActionResult> UpdateDemographics(
        Guid registrationId, [FromBody] UpdateUserDemographicsRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var sanitized = request with { RegistrationId = registrationId };

        try
        {
            await _searchService.UpdateUserDemographicsAsync(jobId!.Value, userId!, sanitized, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{registrationId:guid}/accounting")]
    public async Task<ActionResult<AccountingRecordDto>> CreateAccountingRecord(
        Guid registrationId, [FromBody] CreateAccountingRecordRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var sanitized = request with { RegistrationId = registrationId };

        try
        {
            var record = await _searchService.CreateAccountingRecordAsync(jobId!.Value, userId!, sanitized, ct);
            return Ok(record);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("refund")]
    public async Task<ActionResult<RefundResponse>> ProcessRefund(
        [FromBody] RefundRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _searchService.ProcessRefundAsync(jobId!.Value, userId!, request, ct);
        return Ok(result);
    }

    [HttpGet("payment-methods")]
    public async Task<ActionResult<List<PaymentMethodOptionDto>>> GetPaymentMethods(CancellationToken ct)
    {
        var methods = await _searchService.GetPaymentMethodOptionsAsync(ct);
        return Ok(methods);
    }

    [HttpPost("batch-email")]
    public async Task<ActionResult<BatchEmailResponse>> SendBatchEmail(
        [FromBody] BatchEmailRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var result = await _searchService.SendBatchEmailAsync(jobId!.Value, userId!, request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("email-preview")]
    public async Task<ActionResult<EmailPreviewResponse>> PreviewEmail(
        [FromBody] EmailPreviewRequest request, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required" });

        var result = await _searchService.PreviewEmailAsync(jobId.Value, request, ct);
        return Ok(result);
    }
}
