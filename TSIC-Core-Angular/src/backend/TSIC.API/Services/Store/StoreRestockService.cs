using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Store;

/// <inheritdoc cref="IStoreRestockService"/>
public class StoreRestockService : IStoreRestockService
{
    private readonly IStoreCartRepository _cartRepo;
    private readonly IStoreAnalyticsRepository _analyticsRepo;
    private readonly IStoreRepository _storeRepo;

    public StoreRestockService(
        IStoreCartRepository cartRepo,
        IStoreAnalyticsRepository analyticsRepo,
        IStoreRepository storeRepo)
    {
        _cartRepo = cartRepo;
        _analyticsRepo = analyticsRepo;
        _storeRepo = storeRepo;
    }

    public async Task StageRestockAsync(
        Guid jobId, int storeCartBatchSkuId, int count, string userId, CancellationToken ct = default)
    {
        if (count <= 0)
            throw new InvalidOperationException("Restock count must be at least 1.");

        var store = await _storeRepo.GetByJobIdAsync(jobId, ct)
            ?? throw new InvalidOperationException("Store not found for this job.");

        // Job-scoped, not family-scoped: restock is a STAFF action on a shopper's purchase inside
        // the director's own job. Without the jobId this took a bare line id, so a store admin in
        // one job could put another job's stock back on the shelf and write an audit row there.
        var lineItem = await _cartRepo.GetLineItemInStoreAsync(storeCartBatchSkuId, store.StoreId, ct)
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
