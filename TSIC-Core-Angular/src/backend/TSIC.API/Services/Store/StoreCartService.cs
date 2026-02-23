using TSIC.Contracts.Dtos.Store;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Store;

/// <summary>
/// Service for customer-facing cart operations: browse, add-to-cart, update, checkout.
/// </summary>
public sealed class StoreCartService : IStoreCartService
{
    private readonly IStoreRepository _storeRepo;
    private readonly IStoreCartRepository _cartRepo;
    private readonly IStoreItemRepository _itemRepo;

    public StoreCartService(
        IStoreRepository storeRepo,
        IStoreCartRepository cartRepo,
        IStoreItemRepository itemRepo)
    {
        _storeRepo = storeRepo;
        _cartRepo = cartRepo;
        _itemRepo = itemRepo;
    }

    public async Task<StoreCartBatchDto> GetCurrentCartAsync(Guid jobId, string familyUserId)
    {
        var store = await _storeRepo.GetByJobIdAsync(jobId);
        if (store == null)
            return EmptyCart();

        var cart = await _cartRepo.GetCartAsync(store.StoreId, familyUserId);
        if (cart == null)
            return EmptyCart();

        var batch = await _cartRepo.GetCurrentBatchAsync(cart.StoreCartId);
        if (batch == null)
            return EmptyCart();

        return await BuildCartBatchDto(batch.StoreCartBatchId);
    }

    public async Task<StoreCartBatchDto> AddToCartAsync(
        Guid jobId, string familyUserId, string userId, AddToCartRequest request)
    {
        var store = await _storeRepo.GetByJobIdAsync(jobId)
            ?? throw new InvalidOperationException("Store not found for this job.");

        // Validate SKU exists and is active
        var sku = await _itemRepo.GetSkuByIdAsync(request.StoreSkuId)
            ?? throw new InvalidOperationException("SKU not found.");
        if (!sku.Active)
            throw new InvalidOperationException("SKU is not available.");

        // Check availability
        var soldCount = await _cartRepo.GetSoldCountForSkuAsync(request.StoreSkuId);
        var inCartCount = await _cartRepo.GetInCartCountForSkuAsync(request.StoreSkuId);
        var available = sku.MaxCanSell - soldCount - inCartCount;

        if (request.Quantity > available)
            throw new InvalidOperationException(
                $"Only {available} units available. Requested {request.Quantity}.");

        // Get or create cart and batch
        var (_, batch) = await GetOrCreateCartAndBatch(store.StoreId, familyUserId, userId);

        // Get fee rates
        var config = await _storeRepo.GetJobStoreConfigAsync(jobId)
            ?? throw new InvalidOperationException("Job store config not found.");

        // Check if this SKU is already in the batch (increment instead of duplicating)
        var existingLineItem = await _cartRepo.GetLineItemBySkuAsync(batch.StoreCartBatchId, request.StoreSkuId);

        if (existingLineItem != null)
        {
            existingLineItem.Quantity += request.Quantity;
            RecalculateLineItemFees(existingLineItem, config);
            existingLineItem.Modified = DateTime.UtcNow;
            existingLineItem.LebUserId = userId;
        }
        else
        {
            // Get item price from the SKU's parent item
            var item = await _itemRepo.GetItemByIdAsync(sku.StoreItemId)
                ?? throw new InvalidOperationException("Item not found for SKU.");

            var lineItem = new StoreCartBatchSkus
            {
                StoreCartBatchId = batch.StoreCartBatchId,
                StoreSkuId = request.StoreSkuId,
                DirectToRegId = request.DirectToRegId,
                Active = true,
                Quantity = request.Quantity,
                UnitPrice = item.StoreItemPrice,
                FeeProduct = 0m,
                FeeProcessing = 0m,
                SalesTax = 0m,
                FeeTotal = 0m,
                PaidTotal = 0m,
                RefundedTotal = 0m,
                Restocked = 0,
                CreateDate = DateTime.UtcNow,
                Modified = DateTime.UtcNow,
                LebUserId = userId
            };

            RecalculateLineItemFees(lineItem, config);
            _cartRepo.AddLineItem(lineItem);
        }

        await _cartRepo.SaveChangesAsync();

        return await BuildCartBatchDto(batch.StoreCartBatchId);
    }

    public async Task<StoreCartBatchDto> UpdateQuantityAsync(
        Guid jobId, string familyUserId, string userId,
        int storeCartBatchSkuId, UpdateCartQuantityRequest request)
    {
        if (request.Quantity < 1)
            throw new InvalidOperationException("Quantity must be at least 1.");

        var lineItem = await _cartRepo.GetLineItemByIdAsync(storeCartBatchSkuId)
            ?? throw new InvalidOperationException("Line item not found.");

        // Validate availability for the new quantity
        var sku = await _itemRepo.GetSkuByIdAsync(lineItem.StoreSkuId)
            ?? throw new InvalidOperationException("SKU not found.");

        var soldCount = await _cartRepo.GetSoldCountForSkuAsync(lineItem.StoreSkuId);
        var inCartCount = await _cartRepo.GetInCartCountForSkuAsync(lineItem.StoreSkuId);
        // Subtract current quantity from inCartCount since we're replacing it
        var otherInCart = inCartCount - lineItem.Quantity;
        var available = sku.MaxCanSell - soldCount - otherInCart;

        if (request.Quantity > available)
            throw new InvalidOperationException(
                $"Only {available} units available. Requested {request.Quantity}.");

        var config = await _storeRepo.GetJobStoreConfigAsync(jobId)
            ?? throw new InvalidOperationException("Job store config not found.");

        lineItem.Quantity = request.Quantity;
        RecalculateLineItemFees(lineItem, config);
        lineItem.Modified = DateTime.UtcNow;
        lineItem.LebUserId = userId;

        await _cartRepo.SaveChangesAsync();

        return await BuildCartBatchDto(lineItem.StoreCartBatchId);
    }

    public async Task<StoreCartBatchDto> RemoveFromCartAsync(
        Guid jobId, string familyUserId, string userId, int storeCartBatchSkuId)
    {
        var lineItem = await _cartRepo.GetLineItemByIdAsync(storeCartBatchSkuId)
            ?? throw new InvalidOperationException("Line item not found.");

        var batchId = lineItem.StoreCartBatchId;
        _cartRepo.RemoveLineItem(lineItem);
        await _cartRepo.SaveChangesAsync();

        return await BuildCartBatchDto(batchId);
    }

    public async Task<SkuAvailabilityDto> CheckAvailabilityAsync(int storeSkuId)
    {
        var sku = await _itemRepo.GetSkuByIdAsync(storeSkuId)
            ?? throw new InvalidOperationException("SKU not found.");

        var soldCount = await _cartRepo.GetSoldCountForSkuAsync(storeSkuId);
        var inCartCount = await _cartRepo.GetInCartCountForSkuAsync(storeSkuId);

        return new SkuAvailabilityDto
        {
            StoreSkuId = storeSkuId,
            MaxCanSell = sku.MaxCanSell,
            SoldCount = soldCount,
            InCartCount = inCartCount,
            AvailableCount = sku.MaxCanSell - soldCount - inCartCount
        };
    }

    public async Task<StoreCheckoutResultDto> CheckoutAsync(
        Guid jobId, string familyUserId, string userId, StoreCheckoutRequest request)
    {
        var store = await _storeRepo.GetByJobIdAsync(jobId)
            ?? throw new InvalidOperationException("Store not found for this job.");

        var cart = await _cartRepo.GetCartAsync(store.StoreId, familyUserId)
            ?? throw new InvalidOperationException("No cart found.");

        var batch = await _cartRepo.GetCurrentBatchAsync(cart.StoreCartId)
            ?? throw new InvalidOperationException("No unpaid batch found.");

        // Guard: batch must not already be paid
        var alreadyPaid = await _cartRepo.BatchHasPaymentAsync(batch.StoreCartBatchId);
        if (alreadyPaid)
            throw new InvalidOperationException("This batch has already been paid.");

        // Validate availability of all items
        var overCommitted = await _cartRepo.ValidateBatchAvailabilityAsync(batch.StoreCartBatchId);
        if (overCommitted.Count > 0)
            throw new InvalidOperationException(
                $"Items no longer available: SKU IDs {string.Join(", ", overCommitted)}");

        // Get fee rates and recalculate all line items
        var config = await _storeRepo.GetJobStoreConfigAsync(jobId)
            ?? throw new InvalidOperationException("Job store config not found.");

        var lineItems = await _cartRepo.GetBatchLineItemEntitiesAsync(batch.StoreCartBatchId);
        decimal totalPaid = 0m;

        foreach (var lineItem in lineItems)
        {
            RecalculateLineItemFees(lineItem, config);
            var lineTotal = lineItem.UnitPrice * lineItem.Quantity + lineItem.FeeTotal;
            lineItem.PaidTotal = lineTotal;
            lineItem.Modified = DateTime.UtcNow;
            lineItem.LebUserId = userId;
            totalPaid += lineTotal;
        }

        // Record the payment
        var accounting = new StoreCartBatchAccounting
        {
            StoreCartBatchId = batch.StoreCartBatchId,
            PaymentMethodId = request.PaymentMethodId,
            Paid = totalPaid,
            CreateDate = DateTime.UtcNow,
            Cclast4 = request.Cclast4,
            CcexpDate = request.CcexpDate,
            AdnInvoiceNo = request.AdnInvoiceNo,
            AdnTransactionId = request.AdnTransactionId,
            Comment = request.Comment,
            DiscountCodeAi = request.DiscountCodeAi,
            Modified = DateTime.UtcNow,
            LebUserId = userId
        };

        _cartRepo.AddAccounting(accounting);

        // Single SaveChanges: line item updates + accounting record (implicit transaction)
        await _cartRepo.SaveChangesAsync();

        return new StoreCheckoutResultDto
        {
            StoreCartBatchId = batch.StoreCartBatchId,
            TotalPaid = totalPaid,
            InvoiceNo = request.AdnInvoiceNo
        };
    }

    // ── Private helpers ──

    private async Task<(StoreCart cart, StoreCartBatches batch)> GetOrCreateCartAndBatch(
        int storeId, string familyUserId, string userId)
    {
        var cart = await _cartRepo.GetCartAsync(storeId, familyUserId);

        if (cart == null)
        {
            cart = new StoreCart
            {
                StoreId = storeId,
                FamilyUserId = familyUserId,
                Modified = DateTime.UtcNow,
                LebUserId = userId
            };
            _cartRepo.AddCart(cart);
            await _cartRepo.SaveChangesAsync();
        }

        var batch = await _cartRepo.GetCurrentBatchAsync(cart.StoreCartId);

        if (batch == null)
        {
            batch = new StoreCartBatches
            {
                StoreCartId = cart.StoreCartId,
                Modified = DateTime.UtcNow,
                LebUserId = userId
            };
            _cartRepo.AddBatch(batch);
            await _cartRepo.SaveChangesAsync();
        }

        return (cart, batch);
    }

    private static void RecalculateLineItemFees(StoreCartBatchSkus lineItem, JobStoreConfig config)
    {
        var subtotal = lineItem.UnitPrice * lineItem.Quantity;
        lineItem.FeeProcessing = Math.Round(subtotal * config.StoreTsicrate / 100m, 2);
        lineItem.SalesTax = Math.Round(subtotal * config.StoreSalesTax / 100m, 2);
        lineItem.FeeProduct = 0m; // No product fee in current implementation
        lineItem.FeeTotal = lineItem.FeeProcessing + lineItem.SalesTax + lineItem.FeeProduct;
    }

    private async Task<StoreCartBatchDto> BuildCartBatchDto(int storeCartBatchId)
    {
        var lineItems = await _cartRepo.GetBatchLineItemsAsync(storeCartBatchId);

        var subtotal = lineItems.Sum(li => li.UnitPrice * li.Quantity);
        var totalFees = lineItems.Sum(li => li.FeeProcessing + li.FeeProduct);
        var totalTax = lineItems.Sum(li => li.SalesTax);
        var grandTotal = subtotal + totalFees + totalTax;

        return new StoreCartBatchDto
        {
            StoreCartBatchId = storeCartBatchId,
            LineItems = lineItems,
            Subtotal = subtotal,
            TotalFees = totalFees,
            TotalTax = totalTax,
            GrandTotal = grandTotal
        };
    }

    private static StoreCartBatchDto EmptyCart() => new()
    {
        StoreCartBatchId = 0,
        LineItems = [],
        Subtotal = 0m,
        TotalFees = 0m,
        TotalTax = 0m,
        GrandTotal = 0m
    };
}
