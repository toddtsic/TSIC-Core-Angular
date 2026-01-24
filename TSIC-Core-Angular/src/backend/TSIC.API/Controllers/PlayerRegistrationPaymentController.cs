using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;
using TSIC.Contracts.Repositories;
using TSIC.API.Services.Players;
using TSIC.API.Services.Teams;
using TSIC.API.Services.Families;
using TSIC.API.Services.Clubs;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Metadata;
using TSIC.API.Services.Shared;
using TSIC.API.Services.Shared.VerticalInsure;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Email;
using TSIC.API.Services.Shared.UsLax;
using TSIC.API.Services.Shared.Jobs;
using Microsoft.Extensions.Logging;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/player-registration")]
public class PlayerRegistrationPaymentController : ControllerBase
{
    private readonly IJobLookupService _jobLookupService;
    private readonly IPaymentService _paymentService;
    private readonly IJobDiscountCodeRepository _discountCodeRepo;
    private readonly ILogger<PlayerRegistrationPaymentController> _logger;

    public PlayerRegistrationPaymentController(
        IJobLookupService jobLookupService,
        IPaymentService paymentService,
        IJobDiscountCodeRepository discountCodeRepo,
        ILogger<PlayerRegistrationPaymentController> logger)
    {
        _jobLookupService = jobLookupService;
        _paymentService = paymentService;
        _discountCodeRepo = discountCodeRepo;
        _logger = logger;
    }

    [HttpPost("submit-payment")]
    [Authorize]
    [ProducesResponseType(typeof(PaymentResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> SubmitPayment([FromBody] PaymentRequestDto request)
    {
        _logger.LogInformation("SubmitPayment invoked: jobPath={JobPath} option={Option}", request?.JobPath, request?.PaymentOption);
        if (request == null || request.CreditCard == null || string.IsNullOrWhiteSpace(request.JobPath))
        {
            return BadRequest(new { message = "Invalid payment request" });
        }

        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(familyUserId)) return Unauthorized();

        var jobId = await _jobLookupService.GetJobIdByPathAsync(request.JobPath);
        if (jobId is null)
            return NotFound(new { message = $"Job not found: {request.JobPath}" });

        var result = await _paymentService.ProcessPaymentAsync(jobId.Value, familyUserId, request, familyUserId);
        _logger.LogInformation("SubmitPayment completed: success={Success} errorCode={ErrorCode} txn={Txn} subId={SubId}", result.Success, result.ErrorCode, result.TransactionId, result.SubscriptionId);

        // Always return structured PaymentResponseDto to client (200) so UI can display message
        // rather than treating gateway / ARB failures as transport errors (400).
        if (!result.Success)
        {
            return Ok(result); // Success=false with Message populated
        }
        return Ok(result);
    }

    [HttpPost("apply-discount")]
    [Authorize]
    [ProducesResponseType(typeof(ApplyDiscountResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> ApplyDiscount([FromBody] ApplyDiscountRequestDto request)
    {
        _logger.LogInformation("ApplyDiscount invoked: jobPath={JobPath} code={Code} items={ItemCount}", request?.JobPath, request?.Code, request?.Items?.Count);
        if (request == null || string.IsNullOrWhiteSpace(request.Code) || request.Items == null || string.IsNullOrWhiteSpace(request.JobPath))
        {
            return BadRequest(new { message = "Invalid request" });
        }

        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(familyUserId)) return Unauthorized();

        var jobId = await _jobLookupService.GetJobIdByPathAsync(request.JobPath);
        if (jobId is null)
            return NotFound(new { message = $"Job not found: {request.JobPath}" });

        var now = DateTime.UtcNow;
        var codeLower = request.Code.Trim().ToLowerInvariant();
        var rec = await _discountCodeRepo.GetActiveCodeAsync(jobId.Value, codeLower, now);

        var response = new ApplyDiscountResponseDto();
        if (rec == null)
        {
            response.Success = false;
            response.Message = "Invalid or expired code";
            response.TotalDiscount = 0m;
            return Ok(response);
        }

        var items = request.Items.Where(i => i != null && i.Amount > 0m && !string.IsNullOrWhiteSpace(i.PlayerId)).ToList();
        if (items.Count == 0)
        {
            response.Success = false;
            response.Message = "Nothing to discount";
            response.TotalDiscount = 0m;
            return Ok(response);
        }

        var total = items.Sum(i => i.Amount);
        var (bAsPercent, codeAmount) = rec.Value;
        var amount = codeAmount ?? 0m;
        if (amount <= 0m || total <= 0m)
        {
            response.Success = false;
            response.Message = "Code has no discount";
            response.TotalDiscount = 0m;
            return Ok(response);
        }

        var perPlayer = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        decimal totalDiscount;

        if (bAsPercent ?? false)
        {
            var pct = amount / 100m;
            foreach (var it in items)
            {
                var d = Math.Round(it.Amount * pct, 2, MidpointRounding.AwayFromZero);
                if (d > 0m) perPlayer[it.PlayerId] = d;
            }
            totalDiscount = perPlayer.Values.Sum();
        }
        else
        {
            var cap = Math.Min(amount, total);
            // Proportional distribution by amount with rounding adjustment
            var weights = items
                .Select(i => new { i.PlayerId, i.Amount, w = i.Amount / total })
                .ToList();
            var prelim = weights
                .Select(w => new { w.PlayerId, d = Math.Round(cap * w.w, 2, MidpointRounding.AwayFromZero) })
                .ToList();
            var sum = prelim.Sum(x => x.d);
            // Adjust rounding drift to ensure sum == cap
            var drift = cap - sum; // could be positive or negative within a few cents
            if (drift != 0m)
            {
                // Apply drift to the item with the largest amount to minimize distortion
                var primary = weights.OrderByDescending(x => x.Amount).First();
                var current = prelim.First(x => x.PlayerId.Equals(primary.PlayerId, StringComparison.OrdinalIgnoreCase)).d;
                var adjusted = current + drift;
                prelim = prelim.Select(x => x.PlayerId.Equals(primary.PlayerId, StringComparison.OrdinalIgnoreCase)
                    ? new { x.PlayerId, d = adjusted }
                    : x).ToList();
            }
            foreach (var p in prelim)
            {
                if (p.d > 0m) perPlayer[p.PlayerId] = p.d;
            }
            totalDiscount = perPlayer.Values.Sum();
        }

        response.Success = totalDiscount > 0m;
        response.TotalDiscount = totalDiscount;
        response.PerPlayer = perPlayer;
        response.Message = response.Success ? "Discount applied" : "No discount applicable";
        _logger.LogInformation("ApplyDiscount completed: success={Success} totalDiscount={TotalDiscount} players={PlayerCount}", response.Success, response.TotalDiscount, response.PerPlayer?.Count);
        return Ok(response);
    }
}
