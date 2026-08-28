using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Store;

/// <inheritdoc cref="IStoreRestockService"/>
public class StoreRestockService : IStoreRestockService
{
    private readonly IStoreCartRepository _cartRepo;
    private readonly IStoreAnalyticsRepository _analyticsRepo;

    public StoreRestockService(
        IStoreCartRepository cartRepo,
        IStoreAnalyticsRepository analyticsRepo)
    {
        _cartRepo = cartRepo;
        _analyticsRepo = analyticsRepo;
    }

    public async Task StageRestockAsync(
        int storeCartBatchSkuId, int count, string userId, CancellationToken ct = default)
    {
        if (count <= 0)
            throw new InvalidOperationException("Restock count must be at least 1.");

        var lineItem = await _cartRepo.GetLineItemByIdAsync(storeCartBatchSkuId, ct)
            ?? throw new InvalidOperationException("Purchase line not found.");

        // You cannot put back more than went out. Without this the availability arithmetic
        // (Quantity - Restocked) goes negative and the SKU reads as over-stocked forever.
        var remaining = lineItem.Quantity - lineItem.Restocked;
        if (count > remaining)
            throw new InvalidOperationException(
                remaining <= 0
                    ? "Every unit on this line has already been restocked."
                    : $"Only {remaining} of the {lineItem.Quantity} purchased can still be restocked.");

        // Half one: the inventory fact.
        lineItem.Restocked += count;
        lineItem.Modified = DateTime.Now;
        lineItem.LebUserId = userId;

        // Half two: the audit fact, and the only source for the Restocked report.
        _analyticsRepo.AddRestock(new StoreCartBatchSkuRestocks
        {
            StoreCartBatchSkuId = storeCartBatchSkuId,
            RestockCount = count,
            Modified = DateTime.Now,
            LebUserId = userId
        });

        // Deliberately no SaveChanges — see IStoreRestockService.
    }
}
