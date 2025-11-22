using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Dtos;
using TSIC.API.Services;
using Microsoft.EntityFrameworkCore;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/registration")]
public class RegistrationPaymentController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly SqlDbContext _db;

    public RegistrationPaymentController(IPaymentService paymentService, SqlDbContext db)
    {
        _paymentService = paymentService;
        _db = db;
    }

    [HttpPost("submit-payment")]
    [Authorize]
    [ProducesResponseType(typeof(PaymentResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> SubmitPayment([FromBody] PaymentRequestDto request)
    {
        if (request == null || request.CreditCard == null)
        {
            return BadRequest(new { message = "Invalid payment request" });
        }

        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (callerId == null) return Unauthorized();
        if (!string.Equals(callerId, request.FamilyUserId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        var result = await _paymentService.ProcessPaymentAsync(request, callerId);

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
        if (request == null || string.IsNullOrWhiteSpace(request.Code) || request.Items == null)
        {
            return BadRequest(new { message = "Invalid request" });
        }

        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (callerId == null) return Unauthorized();
        if (!string.Equals(callerId, request.FamilyUserId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        var now = DateTime.UtcNow;
        var codeLower = request.Code.Trim().ToLowerInvariant();
        var rec = await _db.JobDiscountCodes
            .AsNoTracking()
            .Where(d => d.JobId == request.JobId
                        && d.Active
                        && d.CodeStartDate <= now
                        && d.CodeEndDate >= now
                        && d.CodeName.ToLower() == codeLower)
            .Select(d => new { d.BAsPercent, d.CodeAmount })
            .SingleOrDefaultAsync();

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
        var amount = rec.CodeAmount ?? 0m;
        if (amount <= 0m || total <= 0m)
        {
            response.Success = false;
            response.Message = "Code has no discount";
            response.TotalDiscount = 0m;
            return Ok(response);
        }

        var perPlayer = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        decimal totalDiscount;

        if (rec.BAsPercent)
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
        return Ok(response);
    }
}
