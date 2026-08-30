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
    private readonly IStoreRestockService _restockService;

    public StoreAdminService(
        IStoreRepository storeRepo,
        IStoreAnalyticsRepository analyticsRepo,
        IStoreRestockService restockService)
    {
        _storeRepo = storeRepo;
        _analyticsRepo = analyticsRepo;
        _restockService = restockService;
    }

    // ── Analytics ──

    public async Task<List<StoreSalesPivotDto>> GetSalesPivotAsync(Guid jobId)
    {
        var store = await GetStoreOrThrow(jobId);
        return await _analyticsRepo.GetSalesPivotAsync(store.StoreId);
    }

    /// <summary>
    /// Job-scoped, not store-scoped: legacy's query filters on <c>StoreCart.Store.JobId</c>, and a
    /// job has exactly one store, so there is no store lookup to do first.
    /// </summary>
    public Task<List<StoreQuantityAdjustmentDto>> GetQuantityAdjustmentsAsync(
        Guid jobId, CancellationToken ct = default)
        => _analyticsRepo.GetQuantityAdjustmentsAsync(jobId, ct);

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

    /// <summary>
    /// Manual restock from the admin grid — a unit came back over the counter.
    ///
    /// <para>
    /// Routes through <see cref="IStoreRestockService"/>, which is what actually returns the unit
    /// to sellable inventory. This method used to write only the audit row: the report showed the
    /// restock and the SKU stayed unsellable, because every availability figure is
    /// SUM(Quantity - Restocked) and Restocked was never touched.
    /// </para>
    /// </summary>
    public async Task LogRestockAsync(Guid jobId, string userId, LogRestockRequest request)
    {
        await _restockService.StageRestockAsync(
            jobId, request.StoreCartBatchSkuId, request.RestockCount, userId);

        await _analyticsRepo.SaveChangesAsync();
    }

    // ── Pickup ──

    public async Task SignForPickupAsync(Guid jobId, string userId, SignForPickupRequest request)
    {
        // Job-scoped, exactly as StoreRestockService.StageRestockAsync is. This used to fetch by
        // batch id alone while accepting a jobId it never read, so a store admin in one job could
        // sign an order in another — and the screen's own instruction was to type a batch id.
        var store = await _storeRepo.GetByJobIdAsync(jobId)
            ?? throw new InvalidOperationException("Store not found for this job.");

        var batch = await _analyticsRepo.GetBatchInStoreAsync(request.StoreCartBatchId, store.StoreId)
            ?? throw new InvalidOperationException($"Batch {request.StoreCartBatchId} not found.");

        batch.SignedForDate = DateTime.Now;
        batch.SignedForBy = request.SignedForBy;
        batch.Modified = DateTime.Now;
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
