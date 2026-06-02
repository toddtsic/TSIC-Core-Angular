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
using TSIC.Application.Services.Shared.Discount;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/player-registration")]
public class PlayerRegistrationPaymentController : ControllerBase
{
    private readonly IJobLookupService _jobLookupService;
    private readonly IPaymentService _paymentService;
    private readonly IJobDiscountCodeRepository _discountCodeRepo;
    private readonly IRegistrationAccountingRepository _accountingRepo;
    private readonly ILogger<PlayerRegistrationPaymentController> _logger;
    private readonly IRegistrationRepository _registrations;
    private readonly IPlayerFeeCalculator _feeCalc;
    private readonly IRegistrationFeeAdjustmentService _feeAdjustment;
    private readonly IPaymentStateService _paymentState;

    private static readonly Guid CorrectionMethodId = Guid.Parse("33ECA575-A268-E111-9D56-F04DA202060D");

    public PlayerRegistrationPaymentController(
        IJobLookupService jobLookupService,
        IPaymentService paymentService,
        IJobDiscountCodeRepository discountCodeRepo,
        IRegistrationAccountingRepository accountingRepo,
        IRegistrationRepository registrations,
        IPlayerFeeCalculator feeCalc,
        IRegistrationFeeAdjustmentService feeAdjustment,
        IPaymentStateService paymentState,
        ILogger<PlayerRegistrationPaymentController> logger)
    {
        _jobLookupService = jobLookupService;
        _paymentService = paymentService;
        _discountCodeRepo = discountCodeRepo;
        _accountingRepo = accountingRepo;
        _registrations = registrations;
        _feeCalc = feeCalc;
        _feeAdjustment = feeAdjustment;
        _paymentState = paymentState;
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

    /// <summary>
    /// Submit a player-side eCheck (ACH) payment. Settlement pending (typically 3–5 business days).
    /// </summary>
    [HttpPost("submit-echeck")]
    [Authorize]
    [ProducesResponseType(typeof(PaymentResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> SubmitEcheckPayment([FromBody] PaymentRequestDto request)
    {
        _logger.LogInformation("SubmitEcheckPayment invoked: jobPath={JobPath} option={Option}", request?.JobPath, request?.PaymentOption);
        if (request == null || request.BankAccount == null || string.IsNullOrWhiteSpace(request.JobPath))
        {
            return BadRequest(new { message = "Invalid eCheck payment request" });
        }

        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(familyUserId)) return Unauthorized();

        var jobId = await _jobLookupService.GetJobIdByPathAsync(request.JobPath);
        if (jobId is null)
            return NotFound(new { message = $"Job not found: {request.JobPath}" });

        var result = await _paymentService.ProcessEcheckPaymentAsync(jobId.Value, familyUserId, request, familyUserId);
        _logger.LogInformation("SubmitEcheckPayment completed: success={Success} errorCode={ErrorCode} txn={Txn}", result.Success, result.ErrorCode, result.TransactionId);
        // Mirror SubmitPayment: always 200 with structured DTO so UI surfaces gateway errors as messages.
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

        // Discount-code start/end dates are stored in local AZ server time, not UTC.
        var now = DateTime.Now;
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
        var (discountCodeAi, bAsPercent, codeAmount) = rec.Value;
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

        decimal totalDiscount = 0m;

        // Persist discount to registrations and track per-player results
        var requestedPlayerIds = new HashSet<string>(items.Select(i => i.PlayerId).Where(p => !string.IsNullOrWhiteSpace(p)), StringComparer.OrdinalIgnoreCase);
        var regs = await _registrations.GetByJobAndFamilyUserIdAsync(jobId.Value, familyUserId, activePlayersOnly: true);
        var targetRegs = regs.Where(r => !string.IsNullOrWhiteSpace(r.UserId) && requestedPlayerIds.Contains(r.UserId!)).ToList();

        // Create result entry for each requested player
        var updatedFinancials = new Dictionary<string, RegistrationFinancialsDto>();
        // Job rates (once) so each updated financial carries the canonical eCheck-method owed.
        var echeckState = await _paymentState.ForJobAsync(jobId.Value);

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

            // Discount base is the server-side FeeBase, never the client-submitted (proc-inclusive)
            // amount. Computed via the single canonical calculator shared with the team path.
            var d = DiscountCalculator.Calculate(reg.FeeBase, amount, bAsPercent ?? false);
            if (d <= 0m)
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

            // Recalculate totals using the proc already adjusted by ReduceProcessingFee. Pass it
            // directly (0 included) — collapsing 0 to null makes ComputeTotals fall back to default
            // proc, which would re-inflate a full discount that legitimately zeroed the processing fee.
            var (proc, totalFee) = _feeCalc.ComputeTotals(reg.FeeBase, newDiscount, reg.FeeDonation,
                reg.FeeProcessing);
            reg.FeeDiscount = newDiscount;
            reg.FeeProcessing = proc;
            reg.FeeTotal = totalFee;
            reg.OwedTotal = Math.Max(0m, reg.FeeTotal - reg.PaidTotal);
            reg.Modified = DateTime.Now;
            reg.LebUserId = familyUserId;

            // 100% DC: create correction accounting record so registration isn't invisible in ledger
            if (reg.OwedTotal <= 0m)
            {
                var detail = (bAsPercent ?? false) ? $"{amount:0}%" : $"${amount:0.00}";
                _accountingRepo.Add(new Domain.Entities.RegistrationAccounting
                {
                    RegistrationId = reg.RegistrationId,
                    PaymentMethodId = CorrectionMethodId,
                    DiscountCodeAi = discountCodeAi,
                    Dueamt = d,
                    Payamt = d,
                    Comment = $"DC: {request.Code.Trim()} ({detail})",
                    Active = true,
                    Createdate = DateTime.Now,
                    Modified = DateTime.Now,
                    LebUserId = familyUserId
                });
                reg.PaidTotal += d;
                reg.OwedTotal = Math.Max(0m, reg.FeeTotal - reg.PaidTotal);
            }

            updatedFinancials[reg.UserId!] = new RegistrationFinancialsDto
            {
                FeeBase = reg.FeeBase,
                FeeProcessing = reg.FeeProcessing,
                FeeDiscount = reg.FeeDiscount,
                FeeDonation = reg.FeeDonation,
                FeeLateFee = reg.FeeLatefee,
                FeeTotal = reg.FeeTotal,
                OwedTotal = reg.OwedTotal,
                PaidTotal = reg.PaidTotal,
                EcheckOwedTotal = echeckState.ResolveOwed(reg.OwedTotal, reg.FeeBase, reg.FeeDiscount, reg.FeeLatefee, reg.FeeProcessing).Echeck,
                // Canonical Fee-Adj / TenderPaid (job-level state → no corrections in this path).
                FeeAdj = echeckState.FeeAdjustment(reg.FeeDiscount, reg.FeeLatefee),
                TenderPaid = reg.PaidTotal - echeckState.CorrectionApplied
            };

            totalDiscount += d;

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
