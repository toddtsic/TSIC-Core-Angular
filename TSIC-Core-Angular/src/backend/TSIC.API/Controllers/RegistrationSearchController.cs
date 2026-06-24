using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/registration-search")]
[Authorize(Policy = "AdminOnly")]
public class RegistrationSearchController : ControllerBase
{
    private const string RegistrationContextRequired = "Registration context required";

    private readonly IRegistrationSearchService _searchService;
    private readonly IJobLookupService _jobLookupService;
    private readonly IEmailBatchJobRegistry _batchJobs;
    private readonly IEmailBatchService _emailBatch;
    private readonly IHostEnvironment _env;

    public RegistrationSearchController(
        IRegistrationSearchService searchService,
        IJobLookupService jobLookupService,
        IEmailBatchJobRegistry batchJobs,
        IEmailBatchService emailBatch,
        IHostEnvironment env)
    {
        _searchService = searchService;
        _jobLookupService = jobLookupService;
        _batchJobs = batchJobs;
        _emailBatch = emailBatch;
        _env = env;
    }

    private async Task<(Guid? jobId, string? userId, ActionResult? error)> ResolveContext()
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return (null, null, BadRequest(new { message = RegistrationContextRequired }));

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
            return BadRequest(new { message = RegistrationContextRequired });

        var result = await _searchService.SearchAsync(jobId.Value, request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Action-style lookup: Authorize.net live query for subscriptions with cards
    /// expiring this month for this job, returned via the standard search response
    /// shape. Bypasses filter state by design — dropped registrations still need to
    /// surface so admins can follow up on balances their next auto-bill would fail
    /// to collect.
    /// </summary>
    [HttpPost("arb-card-expiring-lookup")]
    public async Task<ActionResult<RegistrationSearchResponse>> ArbCardExpiringLookup(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = RegistrationContextRequired });

        var result = await _searchService.ArbCardExpiringLookupAsync(jobId.Value, ct);
        return Ok(result);
    }

    [HttpGet("filter-options")]
    public async Task<ActionResult<RegistrationFilterOptionsDto>> GetFilterOptions(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = RegistrationContextRequired });

        var options = await _searchService.GetFilterOptionsAsync(jobId.Value, ct);
        return Ok(options);
    }

    /// <summary>
    /// Returns the CADT tree (Club → Agegroup → Division → Team) for team ownership filtering.
    /// Empty array when no teams have club rep assignments.
    /// </summary>
    [HttpGet("cadt-tree")]
    public async Task<ActionResult<List<CadtClubNode>>> GetCadtTree(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = RegistrationContextRequired });

        var tree = await _searchService.GetCadtTreeAsync(jobId.Value, ct);
        return Ok(tree);
    }

    [HttpGet("{registrationId:guid}")]
    public async Task<ActionResult<RegistrationDetailDto>> GetRegistrationDetail(
        Guid registrationId, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = RegistrationContextRequired });

        var detail = await _searchService.GetRegistrationDetailAsync(registrationId, jobId.Value, ct);
        if (detail == null)
            return NotFound();

        return Ok(detail);
    }

    [HttpGet("{registrationId:guid}/family-accounting")]
    public async Task<ActionResult<FamilyAccountingDto>> GetFamilyAccounting(
        Guid registrationId, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = RegistrationContextRequired });

        var result = await _searchService.GetFamilyAccountingAsync(registrationId, jobId.Value, ct);
        if (result == null)
            return NotFound();

        return Ok(result);
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

    [HttpPut("{registrationId:guid}/family-demographics")]
    public async Task<ActionResult> UpdateFamilyAccountDemographics(
        Guid registrationId, [FromBody] UpdateUserDemographicsRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var sanitized = request with { RegistrationId = registrationId };

        try
        {
            await _searchService.UpdateFamilyAccountDemographicsAsync(jobId!.Value, userId!, sanitized, ct);
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

    [HttpPost("{registrationId:guid}/charge-cc")]
    public async Task<ActionResult<RegistrationCcChargeResponse>> ChargeCc(
        Guid registrationId, [FromBody] RegistrationCcChargeRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var sanitized = request with { RegistrationId = registrationId };

        try
        {
            var result = await _searchService.ChargeCcAsync(jobId!.Value, userId!, sanitized, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{registrationId:guid}/record-payment")]
    public async Task<ActionResult<RegistrationCheckOrCorrectionResponse>> RecordPayment(
        Guid registrationId, [FromBody] RegistrationCheckOrCorrectionRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var sanitized = request with { RegistrationId = registrationId };

        try
        {
            var result = await _searchService.RecordCheckOrCorrectionAsync(jobId!.Value, userId!, sanitized, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("accounting/{aId:int}")]
    public async Task<ActionResult> EditAccountingRecord(
        int aId, [FromBody] EditAccountingRecordRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            await _searchService.EditAccountingRecordAsync(jobId!.Value, userId!, aId, request, ct);
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

    [HttpGet("{registrationId:guid}/subscription")]
    public async Task<ActionResult<SubscriptionDetailDto>> GetSubscription(
        Guid registrationId, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var detail = await _searchService.GetSubscriptionDetailAsync(jobId!.Value, registrationId, ct);
        if (detail == null)
            return NotFound();

        return Ok(detail);
    }

    [HttpPost("{registrationId:guid}/cancel-subscription")]
    public async Task<ActionResult> CancelSubscription(
        Guid registrationId, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            await _searchService.CancelSubscriptionAsync(jobId!.Value, userId!, registrationId, ct);
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

    [HttpPut("{registrationId:guid}/email-opt-out")]
    public async Task<ActionResult> SetEmailOptOut(
        Guid registrationId, [FromBody] SetEmailOptOutRequest request, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            await _searchService.SetEmailOptOutAsync(jobId!.Value, registrationId, request.OptOut, ct);
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

    [HttpPut("{registrationId:guid}/active")]
    public async Task<ActionResult> SetActive(
        Guid registrationId, [FromBody] SetActiveRequest request, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            await _searchService.SetActiveAsync(jobId!.Value, registrationId, request.Active, ct);
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

    /// <summary>
    /// Starts a batch send as a background job and returns immediately with a job handle.
    /// Poll <c>batch-email/{jobId}/status</c> for progress + final summary.
    /// </summary>
    [HttpPost("batch-email")]
    public async Task<ActionResult<EmailBatchHandle>> SendBatchEmail(
        [FromBody] BatchEmailRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        // The simulated-send knob is a TEST signal. In Production we REJECT a flagged request
        // outright rather than stripping the flag and proceeding — stripping would silently turn a
        // "test" into a real blast. Rejecting keeps the invariant: a flagged request never reaches
        // the engine in Production, so the simulate flag can never produce a real send anywhere.
        if (request.SimulatedPerUnitDelayMs.HasValue && _env.IsLiveProduction())
            return BadRequest(new { message = "Simulated batch processing is not permitted in Production." });

        try
        {
            var handle = await _searchService.StartBatchEmailAsync(jobId!.Value, userId!, request, ct);
            return Ok(handle);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Progress / final summary for a background batch-email job (404 if unknown/expired).</summary>
    [HttpGet("batch-email/{batchJobId:guid}/status")]
    public ActionResult<EmailBatchJobStatus> GetBatchEmailStatus(Guid batchJobId)
    {
        var status = _batchJobs.Get(batchJobId);
        return status is null ? NotFound() : Ok(status);
    }

    /// <summary>Opt-in: emails the completion summary for a batch to the requesting admin's account email.</summary>
    [HttpPost("batch-email/{batchJobId:guid}/email-summary")]
    public async Task<ActionResult> EmailBatchSummary(Guid batchJobId, CancellationToken ct)
    {
        var (_, userId, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _emailBatch.EmailSummaryAsync(batchJobId, userId!, ct);
        return result switch
        {
            EmailBatchSummaryResult.Sent => NoContent(),
            EmailBatchSummaryResult.UnknownJob => NotFound(),
            EmailBatchSummaryResult.NoRecipientEmail => BadRequest(new { message = "No email address on file for your account." }),
            _ => StatusCode(500)
        };
    }

    [HttpPost("email-preview")]
    public async Task<ActionResult<EmailPreviewResponse>> PreviewEmail(
        [FromBody] EmailPreviewRequest request, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = RegistrationContextRequired });

        var result = await _searchService.PreviewEmailAsync(jobId.Value, request, ct);
        return Ok(result);
    }

    [HttpGet("invite-target-jobs")]
    public async Task<ActionResult<List<JobOptionDto>>> GetInviteTargetJobs(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = RegistrationContextRequired });

        var options = await _searchService.GetChangeJobOptionsAsync(jobId.Value, ct);
        return Ok(options);
    }

    [HttpGet("clubrep-invite-target-jobs")]
    public async Task<ActionResult<List<JobOptionDto>>> GetClubRepInviteTargetJobs(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = RegistrationContextRequired });

        var options = await _searchService.GetFutureJobOptionsAsync(jobId.Value, ct);
        return Ok(options);
    }

    [HttpGet("change-job-options")]
    public async Task<ActionResult<List<JobOptionDto>>> GetChangeJobOptions(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = RegistrationContextRequired });

        var options = await _searchService.GetChangeJobOptionsAsync(jobId.Value, ct);
        return Ok(options);
    }

    [HttpPost("{registrationId:guid}/change-job")]
    public async Task<ActionResult<ChangeJobResponse>> ChangeJob(
        Guid registrationId, [FromBody] ChangeJobRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _searchService.ChangeRegistrationJobAsync(jobId!.Value, userId!, registrationId, request, ct);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    [HttpDelete("{registrationId:guid}")]
    public async Task<ActionResult<DeleteRegistrationResponse>> DeleteRegistration(
        Guid registrationId, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var callerRole = User.FindFirstValue(ClaimTypes.Role) ?? "";
        var result = await _searchService.DeleteRegistrationAsync(
            jobId!.Value, userId!, callerRole, registrationId, ct);

        if (!result.Success)
            return Conflict(result);

        return Ok(result);
    }
}
