using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Shared;

/// <summary>
/// Minimal accounting service extracted from legacy production code. Responsible only for discount calculation.
/// </summary>
public sealed class AccountingService : IAccountingService
{
    public async Task<decimal?> CalculateDiscountFromAccountingRecordAsync(SqlDbContext context, int discountCodeAi, decimal payAmount)
    {
        // Guard clauses
        if (discountCodeAi <= 0) return 0m;

        var jdcRecord = await context.JobDiscountCodes
            .AsNoTracking()
            .Where(jdc => jdc.Ai == discountCodeAi)
            .SingleOrDefaultAsync();

        if (jdcRecord == null) return 0m;

        var amount = jdcRecord.CodeAmount ?? 0m;
        if (jdcRecord.BAsPercent)
        {
            return payAmount * (amount / 100m);
        }
        return amount;
    }
}
