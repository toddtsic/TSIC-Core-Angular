using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TSIC.Application.Services.Shared;
using TSIC.Infrastructure.Data.SqlDbContext;

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
    private readonly SqlDbContext _db;

    public DiscountCodeEvaluatorService(SqlDbContext db) => _db = db;

    public async Task<decimal> EvaluateAsync(int discountCodeAi, decimal baseAmount)
    {
        if (discountCodeAi <= 0 || baseAmount <= 0m) return 0m;

        // Data access: retrieve discount configuration from database
        var rec = await _db.JobDiscountCodes
            .AsNoTracking()
            .Where(d => d.Ai == discountCodeAi)
            .Select(d => new { d.BAsPercent, d.CodeAmount })
            .SingleOrDefaultAsync();

        if (rec == null) return 0m;

        var discountValue = rec.CodeAmount ?? 0m;
        if (discountValue <= 0m) return 0m;

        // Business logic: calculate discount using pure business rules
        return DiscountCalculator.Calculate(baseAmount, discountValue, rec.BAsPercent);
    }
}


