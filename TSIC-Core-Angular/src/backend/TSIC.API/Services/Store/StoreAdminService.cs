using TSIC.Contracts.Dtos.Store;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Store;

/// <summary>
/// Service for store admin operations: analytics, refunds, restocks, pickup signing.
/// </summary>
public sealed class StoreAdminService : IStoreAdminService
{
    private readonly IStoreRepository _storeRepo;
    private readonly IStoreAnalyticsRepository _analyticsRepo;

    public StoreAdminService(
        IStoreRepository storeRepo,
        IStoreAnalyticsRepository analyticsRepo)
    {
        _storeRepo = storeRepo;
        _analyticsRepo = analyticsRepo;
    }

    // ── Analytics ──

    public async Task<List<StoreSalesPivotDto>> GetSalesPivotAsync(Guid jobId)
    {
        var store = await GetStoreOrThrow(jobId);
        return await _analyticsRepo.GetSalesPivotAsync(store.StoreId);
    }

    public async Task<List<StoreSalesByItemDto>> GetSalesByItemAsync(Guid jobId)
    {
        var store = await GetStoreOrThrow(jobId);
        return await _analyticsRepo.GetSalesByItemAsync(store.StoreId);
    }

    public async Task<List<StorePaymentDetailDto>> GetPaymentDetailsAsync(Guid jobId, bool walkUpOnly)
    {
        var store = await GetStoreOrThrow(jobId);
        return await _analyticsRepo.GetPaymentDetailsAsync(store.StoreId, walkUpOnly);
    }

    public async Task<List<StoreFamilyPurchaseDto>> GetFamilyPurchasesAsync(Guid jobId)
    {
        var store = await GetStoreOrThrow(jobId);
        return await _analyticsRepo.GetFamilyPurchasesAsync(store.StoreId);
    }

    public async Task<StoreFamilyPurchaseDto?> GetFamilyPurchaseHistoryAsync(Guid jobId, string familyUserId)
    {
        var store = await GetStoreOrThrow(jobId);
        return await _analyticsRepo.GetFamilyPurchaseHistoryAsync(store.StoreId, familyUserId);
    }

    // ── Refunds ──

    public async Task<List<StoreRefundedItemDto>> GetRefundedItemsAsync(Guid jobId)
    {
        var store = await GetStoreOrThrow(jobId);
        return await _analyticsRepo.GetRefundedItemsAsync(store.StoreId);
    }

    // ── Restocks ──

    public async Task<List<StoreRestockedItemDto>> GetRestockedItemsAsync(Guid jobId)
    {
        var store = await GetStoreOrThrow(jobId);
        return await _analyticsRepo.GetRestockedItemsAsync(store.StoreId);
    }

    public async Task LogRestockAsync(Guid jobId, string userId, LogRestockRequest request)
    {
        var restock = new StoreCartBatchSkuRestocks
        {
            StoreCartBatchSkuId = request.StoreCartBatchSkuId,
            RestockCount = request.RestockCount,
            Modified = DateTime.UtcNow,
            LebUserId = userId
        };

        _analyticsRepo.AddRestock(restock);
        await _analyticsRepo.SaveChangesAsync();
    }

    // ── Pickup ──

    public async Task SignForPickupAsync(Guid jobId, string userId, SignForPickupRequest request)
    {
        var batch = await _analyticsRepo.GetBatchByIdAsync(request.StoreCartBatchId)
            ?? throw new InvalidOperationException($"Batch {request.StoreCartBatchId} not found.");

        batch.SignedForDate = DateTime.UtcNow;
        batch.SignedForBy = request.SignedForBy;
        batch.Modified = DateTime.UtcNow;
        batch.LebUserId = userId;

        await _analyticsRepo.SaveChangesAsync();
    }

    // ── Private helpers ──

    private async Task<Stores> GetStoreOrThrow(Guid jobId)
    {
        return await _storeRepo.GetByJobIdAsync(jobId)
            ?? throw new InvalidOperationException("Store not found for this job.");
    }
}
