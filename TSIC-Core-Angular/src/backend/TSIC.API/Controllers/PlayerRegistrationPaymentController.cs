using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Security.Claims;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Clubs;
using TSIC.API.Services.Email;
using TSIC.API.Services.Families;
using TSIC.API.Services.Metadata;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Players;
using TSIC.API.Services.Shared;
using TSIC.API.Services.Shared.Jobs;
using TSIC.API.Services.Shared.UsLax;
using TSIC.API.Services.Shared.VerticalInsure;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/player-registration")]
public class PlayerRegistrationPaymentController : ControllerBase
{
    private readonly IJobLookupService _jobLookupService;
    private readonly IPaymentService _paymentService;
    private readonly IJobDiscountCodeRepository _discountCodeRepo;
    private readonly ILogger<PlayerRegistrationPaymentController> _logger;
    private readonly IRegistrationRepository _registrations;
    private readonly IRegistrationRecordFeeCalculatorService _feeCalc;
    private readonly IRegistrationFeeAdjustmentService _feeAdjustment;

    public PlayerRegistrationPaymentController(
        IJobLookupService jobLookupService,
        IPaymentService paymentService,
        IJobDiscountCodeRepository discountCodeRepo,
        IRegistrationRepository registrations,
        IRegistrationRecordFeeCalculatorService feeCalc,
        IRegistrationFeeAdjustmentService feeAdjustment,
        ILogger<PlayerRegistrationPaymentController> logger)
    {
        _jobLookupService = jobLookupService;
        _paymentService = paymentService;
        _discountCodeRepo = discountCodeRepo;
        _registrations = registrations;
        _feeCalc = feeCalc;
        _feeAdjustment = feeAdjustment;
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

        var results = new List<PlayerDiscountResult>();
        if (rec == null)
        {
            return Ok(new ApplyDiscountResponseDto
            {
                Success = false,
                Message = "Invalid or expired discount code",
                TotalDiscount = 0m,
                TotalPlayersProcessed = 0,
                SuccessCount = 0,
                FailureCount = 0,
                Results = results,
                UpdatedFinancials = new()
            });
        }

        var items = request.Items.Where(i => i != null && i.Amount > 0m && !string.IsNullOrWhiteSpace(i.PlayerId)).ToList();
        if (items.Count == 0)
        {
            return Ok(new ApplyDiscountResponseDto
            {
                Success = false,
                Message = "No valid players for discount",
                TotalDiscount = 0m,
                TotalPlayersProcessed = 0,
                SuccessCount = 0,
                FailureCount = 0,
                Results = results,
                UpdatedFinancials = new()
            });
        }

        var total = items.Sum(i => i.Amount);
        var (bAsPercent, codeAmount) = rec.Value;
        var amount = codeAmount ?? 0m;
        if (amount <= 0m || total <= 0m)
        {
            return Ok(new ApplyDiscountResponseDto
            {
                Success = false,
                Message = "Discount code has no discount amount",
                TotalDiscount = 0m,
                TotalPlayersProcessed = 0,
                SuccessCount = 0,
                FailureCount = 0,
                Results = results,
                UpdatedFinancials = new()
            });
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

        // Persist discount to registrations and track per-player results
        var requestedPlayerIds = new HashSet<string>(items.Select(i => i.PlayerId).Where(p => !string.IsNullOrWhiteSpace(p)), StringComparer.OrdinalIgnoreCase);
        var regs = await _registrations.GetByJobAndFamilyUserIdAsync(jobId.Value, familyUserId, activePlayersOnly: true);
        var targetRegs = regs.Where(r => !string.IsNullOrWhiteSpace(r.UserId) && requestedPlayerIds.Contains(r.UserId!)).ToList();
        
        // Create result entry for each requested player
        var updatedFinancials = new Dictionary<string, RegistrationFinancialsDto>();
        
        foreach (var item in items)
        {
            var reg = targetRegs.FirstOrDefault(r => r.UserId?.Equals(item.PlayerId, StringComparison.OrdinalIgnoreCase) ?? false);
            
            if (reg == null)
            {
                results.Add(new PlayerDiscountResult
                {
                    PlayerId = item.PlayerId,
                    PlayerName = "Unknown",
                    Success = false,
                    Message = "Player registration not found",
                    DiscountAmount = 0m
                });
                continue;
            }

            // Check if already discounted
            if (reg.FeeDiscount > 0m)
            {
                results.Add(new PlayerDiscountResult
                {
                    PlayerId = item.PlayerId,
                    PlayerName = reg.InsuredName ?? "Unknown",
                    Success = false,
                    Message = "Discount already applied to this player",
                    DiscountAmount = 0m
                });
                continue;
            }

            // Check if discount exists for this player
            if (!perPlayer.TryGetValue(item.PlayerId, out var d) || d <= 0m)
            {
                results.Add(new PlayerDiscountResult
                {
                    PlayerId = item.PlayerId,
                    PlayerName = reg.InsuredName ?? "Unknown",
                    Success = false,
                    Message = "No discount applicable",
                    DiscountAmount = 0m
                });
                continue;
            }

            // Apply discount
            var newDiscount = reg.FeeDiscount + d;

            // Proportionally reduce processing fee by discount amount (discount reduces CC transaction)
            await _feeAdjustment.ReduceProcessingFeeProportionalAsync(reg, d, jobId.Value, familyUserId);

            // Recalculate totals with updated processing fee
            var (proc, totalFee) = _feeCalc.ComputeTotals(reg.FeeBase, newDiscount, reg.FeeDonation,
                reg.FeeProcessing > 0m ? reg.FeeProcessing : null);
            reg.FeeDiscount = newDiscount;
            reg.FeeProcessing = proc;
            reg.FeeTotal = totalFee;
            reg.OwedTotal = Math.Max(0m, reg.FeeTotal - reg.PaidTotal);
            reg.Modified = DateTime.UtcNow;
            reg.LebUserId = familyUserId;

            updatedFinancials[reg.UserId!] = new RegistrationFinancialsDto
            {
                FeeBase = reg.FeeBase,
                FeeProcessing = reg.FeeProcessing,
                FeeDiscount = reg.FeeDiscount,
                FeeDonation = reg.FeeDonation,
                FeeLateFee = reg.FeeLatefee,
                FeeTotal = reg.FeeTotal,
                OwedTotal = reg.OwedTotal,
                PaidTotal = reg.PaidTotal
            };

            results.Add(new PlayerDiscountResult
            {
                PlayerId = item.PlayerId,
                PlayerName = reg.InsuredName ?? "Unknown",
                Success = true,
                Message = $"Discount applied: {d:C}",
                DiscountAmount = d
            });
        }

        await _registrations.SaveChangesAsync();

        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count(r => !r.Success);

        var response = new ApplyDiscountResponseDto
        {
            Success = successCount > 0,
            Message = successCount > 0 ? $"Successfully applied discount to {successCount} player(s)" : "No discounts were applied",
            TotalDiscount = totalDiscount,
            TotalPlayersProcessed = results.Count,
            SuccessCount = successCount,
            FailureCount = failureCount,
            Results = results,
            UpdatedFinancials = updatedFinancials
        };

        _logger.LogInformation("ApplyDiscount completed: success={Success} totalDiscount={TotalDiscount} processed={Processed} succeeded={Succeeded} failed={Failed}", 
            response.Success, response.TotalDiscount, response.TotalPlayersProcessed, successCount, failureCount);
        return Ok(response);
    }
}
