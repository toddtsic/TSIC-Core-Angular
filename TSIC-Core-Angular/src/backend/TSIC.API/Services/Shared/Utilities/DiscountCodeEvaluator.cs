using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TSIC.Application.Services.Shared.Discount;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Shared.Utilities;

public interface IDiscountCodeEvaluator
{
    /// <summary>
    /// Evaluate a discount code against a base amount and return the discount value (never negative).
    /// Returns 0 when the code is invalid, missing, or results in zero discount.
    /// </summary>
    Task<decimal> EvaluateAsync(int discountCodeAi, decimal baseAmount);
}

/// <summary>
/// Single-point-of-truth for discount code evaluation (percent or fixed amount).
/// Intentionally minimal; validation rules (date windows, job scope, stacking) can be layered later.
/// </summary>
public sealed class DiscountCodeEvaluatorService : IDiscountCodeEvaluator
{
    private readonly IJobDiscountCodeRepository _discountCodeRepo;

    public DiscountCodeEvaluatorService(IJobDiscountCodeRepository discountCodeRepo) => _discountCodeRepo = discountCodeRepo;

    public async Task<decimal> EvaluateAsync(int discountCodeAi, decimal baseAmount)
    {
        if (discountCodeAi <= 0 || baseAmount <= 0m) return 0m;

        // Data access: retrieve discount configuration via repository
        var rec = await _discountCodeRepo.GetByAiAsync(discountCodeAi);

        if (rec == null) return 0m;

        var discountValue = rec.Value.CodeAmount ?? 0m;
        if (discountValue <= 0m) return 0m;

        // Business logic: calculate discount using pure business rules
        return DiscountCalculator.Calculate(baseAmount, discountValue, rec.Value.BAsPercent ?? false);
    }
}


