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
using TSIC.Contracts.Payments;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Contracts.Extensions;
using TSIC.Application.Services.Shared.Discount;

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
    private readonly IRegistrationFeeAdjustmentService _feeAdjustment;
    private readonly IPaymentStateService _paymentState;
    private readonly IVerticalInsureService _verticalInsure;

    public PlayerRegistrationPaymentController(
        IJobLookupService jobLookupService,
        IPaymentService paymentService,
        IJobDiscountCodeRepository discountCodeRepo,
        IRegistrationRepository registrations,
        IRegistrationFeeAdjustmentService feeAdjustment,
        IPaymentStateService paymentState,
        IVerticalInsureService verticalInsure,
        ILogger<PlayerRegistrationPaymentController> logger)
    {
        _jobLookupService = jobLookupService;
        _paymentService = paymentService;
        _discountCodeRepo = discountCodeRepo;
        _registrations = registrations;
        _feeAdjustment = feeAdjustment;
        _paymentState = paymentState;
        _verticalInsure = verticalInsure;
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

        // A player can register for multiple camps in one job — each is its own reg row (same
        // UserId, distinct AssignedTeamId). Match each item to its specific camp via TeamId, and
        // track consumed reg rows so a player with N camps gets N discounts (not just the first).
        var processedRegIds = new HashSet<Guid>();

        foreach (var item in items)
        {
            var reg = targetRegs.FirstOrDefault(r =>
                (r.UserId?.Equals(item.PlayerId, StringComparison.OrdinalIgnoreCase) ?? false)
                && (item.TeamId == null || r.AssignedTeamId == item.TeamId)
                && !processedRegIds.Contains(r.RegistrationId));

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

            // Consume this reg row so the next item for the same player (another camp) matches a
            // different row — even on the TeamId-absent fallback path.
            processedRegIds.Add(reg.RegistrationId);

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

            // Set the new discount, record which code on the reg, then recompute totals through the
            // single canonical helper (RecalcTotals → FeeMath). FeeProcessing was already adjusted in
            // place by ReduceProcessingFeeProportionalAsync above (and stays ≥0), so it flows straight
            // through (0 included) — a full discount that legitimately zeroed the proc is not re-inflated.
            // OwedTotal is now signed (RecalcTotals drops the old Math.Max clamp) per the signed-owed policy.
            reg.FeeDiscount = newDiscount;
            // Which code, recorded on the registration itself — the canonical redemption-count
            // source (JobDiscountCodeRepository.GetUsageCountAsync reads reg.DiscountCodeId).
            reg.DiscountCodeId = discountCodeAi;
            reg.RecalcTotals();
            // A full waiver (100% code, or a fixed code that clears the balance) zeroes OwedTotal:
            // the registration owes nothing, so it must be activated here. Activation otherwise rides
            // ProcessPaymentAsync's charge path, which never runs when there is no payment. This
            // mirrors the legacy "owes nothing → active" rule (PlayerBaseController) and replaces the
            // activation side-effect of the removed fake "Correction" payment a 100% DC used to stamp.
            // Partial discounts leave OwedTotal > 0 and still activate on payment, as before.
            if (reg.OwedTotal <= 0m)
            {
                reg.BActive = true;
            }
            reg.Modified = DateTime.Now;
            reg.LebUserId = familyUserId;

            // A discount is a fee modifier, not a payment: it reduces FeeTotal and is recorded on the
            // reg (DiscountCodeId) — it never writes a RegistrationAccounting row or PaidTotal. (A 100% DC
            // used to stamp a fake Correction Payamt + PaidTotal +=, double-booking the discount and —
            // under signed OwedTotal — surfacing a phantom -discount.)

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
                EcheckOwedTotal = echeckState.ResolveOwed(reg.OwedTotal, reg.FeeBase, reg.FeeDiscount, reg.FeeLatefee, reg.FeeDonation, reg.FeeProcessing).Echeck,
                CheckOwedTotal = echeckState.ResolveOwed(reg.OwedTotal, reg.FeeBase, reg.FeeDiscount, reg.FeeLatefee, reg.FeeDonation, reg.FeeProcessing).Check,
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

        // Rebuild the VerticalInsure offer off the now-persisted discount so the embedded widget
        // can remount with the corrected insurable amount. Same builder PreSubmit uses, run against
        // the just-saved regs — not a competing source of truth. Only when something actually
        // changed; BuildOfferAsync self-gates to Available=false when VI isn't offered for the job.
        PreSubmitInsuranceDto? insuranceOffer = null;
        if (successCount > 0)
        {
            insuranceOffer = await _verticalInsure.BuildOfferAsync(jobId.Value, familyUserId);
        }

        var response = new ApplyDiscountResponseDto
        {
            Success = successCount > 0,
            Message = successCount > 0 ? $"Successfully applied discount to {successCount} player(s)" : "No discounts were applied",
            TotalDiscount = totalDiscount,
            TotalPlayersProcessed = results.Count,
            SuccessCount = successCount,
            FailureCount = failureCount,
            Results = results,
            UpdatedFinancials = updatedFinancials,
            InsuranceOffer = insuranceOffer
        };

        _logger.LogInformation("ApplyDiscount completed: success={Success} totalDiscount={TotalDiscount} processed={Processed} succeeded={Succeeded} failed={Failed}",
            response.Success, response.TotalDiscount, response.TotalPlayersProcessed, successCount, failureCount);
        return Ok(response);
    }
}
