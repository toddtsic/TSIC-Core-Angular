using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.API.Services.Shared;

public interface IAccountingService
{
    Task<decimal?> CalculateDiscountFromAccountingRecordAsync(SqlDbContext context, int discountCodeAi, decimal payAmount);
}
