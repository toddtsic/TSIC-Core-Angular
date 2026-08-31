using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Admin;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.CustomerJobRevenue;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/customer-job-revenue")]
[Authorize(Policy = "CanCrossCustomerJobs")]
public class CustomerJobRevenueController : ControllerBase
{
    private readonly ICustomerJobRevenueService _revenueService;
    private readonly IJobLookupService _jobLookupService;
    private readonly IHostEnvironment _env;

    public CustomerJobRevenueController(
        ICustomerJobRevenueService revenueService,
        IJobLookupService jobLookupService,
        IHostEnvironment env)
    {
        _revenueService = revenueService;
        _jobLookupService = jobLookupService;
        _env = env;
    }

    /// <summary>
    /// Available job names for the caller's customer group — cheap scope-picker query.
    /// The guided flow calls this on page load instead of running the full report.
    /// </summary>
    [HttpGet("jobs")]
    public async Task<ActionResult<List<string>>> GetAvailableJobs(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var jobs = await _revenueService.GetAvailableJobNamesAsync(jobId.Value, ct);
        return Ok(jobs);
    }

    /// <summary>
    /// Scoped revenue rollup (pivot records + monthly counts + admin fees).
    /// Scope guardrail: either explicit jobNames (complete history of exactly those jobs,
    /// dates ignored) or a mandatory valid date range across all jobs — never neither.
    /// </summary>
    [HttpGet("rollup")]
    public async Task<ActionResult<RevenueRollupResponseDto>> GetRollup(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] List<string> jobNames,
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var scopeError = ValidateScope(startDate, endDate, jobNames);
        if (scopeError != null)
        {
            return BadRequest(new { message = scopeError });
        }

        var hasJobScope = jobNames is { Count: > 0 };
        var data = await _revenueService.GetRollupAsync(
            jobId.Value,
            hasJobScope ? null : startDate,
            hasJobScope ? null : endDate,
            jobNames ?? [], ct);

        return Ok(data);
    }

    /// <summary>
    /// Lazy per-tab payment detail records. method: cc | check | echeck.
    /// Same scope guardrail as the rollup.
    /// </summary>
    [HttpGet("details/{method}")]
    public async Task<ActionResult<List<JobPaymentRecordDto>>> GetPaymentDetails(
        string method,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] List<string> jobNames,
        CancellationToken ct)
    {
        if (method is not ("cc" or "check" or "echeck"))
        {
            return BadRequest(new { message = "method must be cc, check, or echeck" });
        }

        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var scopeError = ValidateScope(startDate, endDate, jobNames);
        if (scopeError != null)
        {
            return BadRequest(new { message = scopeError });
        }

        var hasJobScope = jobNames is { Count: > 0 };
        var records = await _revenueService.GetPaymentDetailsAsync(
            jobId.Value, method,
            hasJobScope ? null : startDate,
            hasJobScope ? null : endDate,
            jobNames ?? [], ct);

        return Ok(records);
    }

    /// <summary>
    /// Team Billing tab: every ACTIVE team in scope with its lifetime billed/collected/owed.
    /// Team-driven, so teams that have never paid are included — the payment-driven rollup
    /// cannot see them, and they carry most of the outstanding balance.
    /// </summary>
    /// <remarks>
    /// Same scope guardrail and same query params as the rollup, but the date range bounds
    /// <c>teams.createdate</c> (when the team REGISTERED), not when money moved. Balances are
    /// current, so this tab deliberately does not reconcile month-to-month with the rollup.
    /// </remarks>
    [HttpGet("team-billing")]
    public async Task<ActionResult<List<TeamBillingRecordDto>>> GetTeamBilling(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] List<string> jobNames,
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var scopeError = ValidateScope(startDate, endDate, jobNames);
        if (scopeError != null)
        {
            return BadRequest(new { message = scopeError });
        }

        var hasJobScope = jobNames is { Count: > 0 };
        var records = await _revenueService.GetTeamBillingAsync(
            jobId.Value,
            hasJobScope ? null : startDate,
            hasJobScope ? null : endDate,
            jobNames ?? [], ct);

        return Ok(records);
    }

    /// <summary>
    /// The scope guardrail born of two real overpayment incidents: an unscoped request
    /// (no jobs, no dates) silently aggregated x-job revenue. Reject it server-side.
    /// </summary>
    private static string? ValidateScope(DateTime? startDate, DateTime? endDate, List<string>? jobNames)
    {
        if (jobNames is { Count: > 0 })
        {
            return null; // explicit job scope — complete history, dates ignored
        }
        if (startDate == null || endDate == null)
        {
            return "Scope required: select specific jobs, or provide a full date range for all jobs.";
        }
        if (startDate > endDate)
        {
            return "startDate must be on or before endDate.";
        }
        return null;
    }

    /// <summary>
    /// SuperUser live QA: diff the EF port against the legacy sprocs for the given scope.
    /// Live proof the accounting numbers match legacy — usable any time until cutover.
    /// </summary>
    [HttpGet("legacy-compare")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<ActionResult<LegacyCompareResultDto>> CompareWithLegacy(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] List<string> jobNames,
        CancellationToken ct)
    {
        // SuperUser AND sandbox (Development/Staging) only — 404s in Production like the
        // other IsSandbox()-gated tools. QA runs both engines; prod never pays that double.
        if (!_env.IsSandbox())
        {
            return NotFound();
        }

        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var scopeError = ValidateScope(startDate, endDate, jobNames);
        if (scopeError != null)
        {
            return BadRequest(new { message = scopeError });
        }

        var result = await _revenueService.CompareWithLegacyAsync(
            jobId.Value, startDate, endDate, jobNames ?? [], ct);

        return Ok(result);
    }

    /// <summary>
    /// Inline-edit a single MonthlyJobStats row (Player/Team Counts grid).
    /// </summary>
    [HttpPut("monthly-counts/{aid:int}")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<IActionResult> UpdateMonthlyCount(
        int aid,
        [FromBody] UpdateMonthlyCountRequest request,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        await _revenueService.UpdateMonthlyCountAsync(aid, request, userId, ct);

        return NoContent();
    }
}
