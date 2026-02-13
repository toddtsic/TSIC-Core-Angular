using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Dtos.TeamSearch;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/team-search")]
[Authorize(Policy = "AdminOnly")]
public class TeamSearchController : ControllerBase
{
    private readonly ILogger<TeamSearchController> _logger;
    private readonly ITeamSearchService _teamSearchService;
    private readonly IJobLookupService _jobLookupService;

    public TeamSearchController(
        ILogger<TeamSearchController> logger,
        ITeamSearchService teamSearchService,
        IJobLookupService jobLookupService)
    {
        _logger = logger;
        _teamSearchService = teamSearchService;
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

    // ── Search & Filters ──

    [HttpPost("search")]
    public async Task<ActionResult<TeamSearchResponse>> Search(
        [FromBody] TeamSearchRequest request, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required" });

        var result = await _teamSearchService.SearchAsync(jobId.Value, request, ct);
        return Ok(result);
    }

    [HttpGet("filter-options")]
    public async Task<ActionResult<TeamFilterOptionsDto>> GetFilterOptions(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required" });

        var options = await _teamSearchService.GetFilterOptionsAsync(jobId.Value, ct);
        return Ok(options);
    }

    // ── Team Detail ──

    [HttpGet("{teamId:guid}")]
    public async Task<ActionResult<TeamSearchDetailDto>> GetTeamDetail(Guid teamId, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required" });

        var detail = await _teamSearchService.GetTeamDetailAsync(teamId, jobId.Value, ct);
        if (detail == null)
            return NotFound();

        return Ok(detail);
    }

    [HttpPut("{teamId:guid}")]
    public async Task<ActionResult> EditTeam(
        Guid teamId, [FromBody] EditTeamRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            await _teamSearchService.EditTeamAsync(teamId, jobId!.Value, userId!, request, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ── Team-Level Payment Operations ──

    [HttpPost("{teamId:guid}/cc-charge")]
    public async Task<ActionResult<TeamCcChargeResponse>> ChargeCcForTeam(
        Guid teamId, [FromBody] TeamCcChargeRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var sanitized = request with { TeamId = teamId };
        var result = await _teamSearchService.ChargeCcForTeamAsync(jobId!.Value, userId!, sanitized, ct);
        return Ok(result);
    }

    [HttpPost("{teamId:guid}/check")]
    public async Task<ActionResult<TeamCheckOrCorrectionResponse>> RecordCheckForTeam(
        Guid teamId, [FromBody] TeamCheckOrCorrectionRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var sanitized = request with { TeamId = teamId };
        var result = await _teamSearchService.RecordCheckForTeamAsync(jobId!.Value, userId!, sanitized, ct);
        return Ok(result);
    }

    // ── Club-Level Payment Operations ──

    [HttpPost("club/{clubRepRegId:guid}/cc-charge")]
    public async Task<ActionResult<TeamCcChargeResponse>> ChargeCcForClub(
        Guid clubRepRegId, [FromBody] TeamCcChargeRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var sanitized = request with { ClubRepRegistrationId = clubRepRegId };
        var result = await _teamSearchService.ChargeCcForClubAsync(jobId!.Value, userId!, sanitized, ct);
        return Ok(result);
    }

    [HttpPost("club/{clubRepRegId:guid}/check")]
    public async Task<ActionResult<TeamCheckOrCorrectionResponse>> RecordCheckForClub(
        Guid clubRepRegId, [FromBody] TeamCheckOrCorrectionRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var sanitized = request with { ClubRepRegistrationId = clubRepRegId };
        var result = await _teamSearchService.RecordCheckForClubAsync(jobId!.Value, userId!, sanitized, ct);
        return Ok(result);
    }

    // ── Refund ──

    [HttpPost("refund")]
    public async Task<ActionResult<RefundResponse>> ProcessRefund(
        [FromBody] RefundRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _teamSearchService.ProcessRefundAsync(jobId!.Value, userId!, request, ct);
        return Ok(result);
    }

    // ── Shared ──

    [HttpGet("payment-methods")]
    public async Task<ActionResult<List<PaymentMethodOptionDto>>> GetPaymentMethods(CancellationToken ct)
    {
        var methods = await _teamSearchService.GetPaymentMethodOptionsAsync(ct);
        return Ok(methods);
    }
}
