using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Dtos.Store;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/store")]
[Authorize]
public class StoreController : ControllerBase
{
    private readonly IStoreCatalogService _catalogService;
    private readonly IStoreCartService _cartService;
    private readonly IStoreAdminService _adminService;
    private readonly IJobLookupService _jobLookupService;
    private readonly IStoreWalkUpService _walkUpService;
    private readonly IRegistrationAccountingRepository _accountingRepo;

    public StoreController(
        IStoreCatalogService catalogService,
        IStoreCartService cartService,
        IStoreAdminService adminService,
        IJobLookupService jobLookupService,
        IStoreWalkUpService walkUpService,
        IRegistrationAccountingRepository accountingRepo)
    {
        _catalogService = catalogService;
        _cartService = cartService;
        _adminService = adminService;
        _jobLookupService = jobLookupService;
        _walkUpService = walkUpService;
        _accountingRepo = accountingRepo;
    }

    // ═══════════════════════════════════════════
    //  CATALOG — Admin
    // ═══════════════════════════════════════════

    [HttpGet]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(StoreDto), 200)]
    public async Task<IActionResult> GetOrCreateStore()
    {
        var (jobId, userId) = await ResolveContext();
        var store = await _catalogService.GetOrCreateStoreAsync(jobId, userId);
        return Ok(store);
    }

    [HttpGet("items")]
    [ProducesResponseType(typeof(List<StoreItemSummaryDto>), 200)]
    public async Task<IActionResult> GetItems()
    {
        var (jobId, _) = await ResolveContext();
        var items = await _catalogService.GetItemsAsync(jobId);
        return Ok(items);
    }

    [HttpGet("items/{storeItemId:int}")]
    [ProducesResponseType(typeof(StoreItemDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetItemDetail(int storeItemId)
    {
        var (jobId, _) = await ResolveContext();
        var item = await _catalogService.GetItemDetailAsync(jobId, storeItemId);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [HttpPost("items")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(StoreItemDto), 201)]
    public async Task<IActionResult> CreateItem([FromBody] CreateStoreItemRequest request)
    {
        var (jobId, userId) = await ResolveContext();
        try
        {
            var item = await _catalogService.CreateItemAsync(jobId, userId, request);
            return CreatedAtAction(nameof(GetItemDetail), new { storeItemId = item.StoreItemId }, item);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("items/{storeItemId:int}")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(StoreItemDto), 200)]
    public async Task<IActionResult> UpdateItem(int storeItemId, [FromBody] UpdateStoreItemRequest request)
    {
        var (jobId, userId) = await ResolveContext();
        try
        {
            var item = await _catalogService.UpdateItemAsync(jobId, userId, storeItemId, request);
            return Ok(item);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("items/{storeItemId:int}/skus")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(List<StoreSkuDto>), 200)]
    public async Task<IActionResult> GetSkus(int storeItemId)
    {
        var skus = await _catalogService.GetSkusAsync(storeItemId);
        return Ok(skus);
    }

    [HttpPut("skus/{storeSkuId:int}")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(StoreSkuDto), 200)]
    public async Task<IActionResult> UpdateSku(int storeSkuId, [FromBody] UpdateStoreSkuRequest request)
    {
        var (_, userId) = await ResolveContext();
        try
        {
            var sku = await _catalogService.UpdateSkuAsync(userId, storeSkuId, request);
            return Ok(sku);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ── Colors ──

    [HttpGet("colors")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(List<StoreColorDto>), 200)]
    public async Task<IActionResult> GetColors()
    {
        var colors = await _catalogService.GetColorsAsync();
        return Ok(colors);
    }

    [HttpPost("colors")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(StoreColorDto), 201)]
    public async Task<IActionResult> CreateColor([FromBody] CreateStoreColorRequest request)
    {
        var (_, userId) = await ResolveContext();
        var color = await _catalogService.CreateColorAsync(userId, request);
        return Created("", color);
    }

    [HttpPut("colors/{storeColorId:int}")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(StoreColorDto), 200)]
    public async Task<IActionResult> UpdateColor(int storeColorId, [FromBody] UpdateStoreColorRequest request)
    {
        var (_, userId) = await ResolveContext();
        try
        {
            var color = await _catalogService.UpdateColorAsync(userId, storeColorId, request);
            return Ok(color);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("colors/{storeColorId:int}")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteColor(int storeColorId)
    {
        try
        {
            await _catalogService.DeleteColorAsync(storeColorId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ── Sizes ──

    [HttpGet("sizes")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(List<StoreSizeDto>), 200)]
    public async Task<IActionResult> GetSizes()
    {
        var sizes = await _catalogService.GetSizesAsync();
        return Ok(sizes);
    }

    [HttpPost("sizes")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(StoreSizeDto), 201)]
    public async Task<IActionResult> CreateSize([FromBody] CreateStoreSizeRequest request)
    {
        var (_, userId) = await ResolveContext();
        var size = await _catalogService.CreateSizeAsync(userId, request);
        return Created("", size);
    }

    [HttpPut("sizes/{storeSizeId:int}")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(StoreSizeDto), 200)]
    public async Task<IActionResult> UpdateSize(int storeSizeId, [FromBody] UpdateStoreSizeRequest request)
    {
        var (_, userId) = await ResolveContext();
        try
        {
            var size = await _catalogService.UpdateSizeAsync(userId, storeSizeId, request);
            return Ok(size);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("sizes/{storeSizeId:int}")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteSize(int storeSizeId)
    {
        try
        {
            await _catalogService.DeleteSizeAsync(storeSizeId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ═══════════════════════════════════════════
    //  CART — Customer
    // ═══════════════════════════════════════════

    [HttpGet("cart")]
    [ProducesResponseType(typeof(StoreCartBatchDto), 200)]
    public async Task<IActionResult> GetCurrentCart()
    {
        var (jobId, userId) = await ResolveContext();
        var cart = await _cartService.GetCurrentCartAsync(jobId, userId);
        return Ok(cart);
    }

    [HttpPost("cart/items")]
    [ProducesResponseType(typeof(StoreCartBatchDto), 200)]
    public async Task<IActionResult> AddToCart([FromBody] AddToCartRequest request)
    {
        var (jobId, userId) = await ResolveContext();
        try
        {
            var cart = await _cartService.AddToCartAsync(jobId, userId, userId, request);
            return Ok(cart);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("cart/items/{storeCartBatchSkuId:int}/quantity")]
    [ProducesResponseType(typeof(StoreCartBatchDto), 200)]
    public async Task<IActionResult> UpdateQuantity(
        int storeCartBatchSkuId, [FromBody] UpdateCartQuantityRequest request)
    {
        var (jobId, userId) = await ResolveContext();
        try
        {
            var cart = await _cartService.UpdateQuantityAsync(jobId, userId, userId, storeCartBatchSkuId, request);
            return Ok(cart);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("cart/items/{storeCartBatchSkuId:int}")]
    [ProducesResponseType(typeof(StoreCartBatchDto), 200)]
    public async Task<IActionResult> RemoveFromCart(int storeCartBatchSkuId)
    {
        var (jobId, userId) = await ResolveContext();
        try
        {
            var cart = await _cartService.RemoveFromCartAsync(jobId, userId, userId, storeCartBatchSkuId);
            return Ok(cart);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("skus/{storeSkuId:int}/availability")]
    [ProducesResponseType(typeof(SkuAvailabilityDto), 200)]
    public async Task<IActionResult> CheckAvailability(int storeSkuId)
    {
        try
        {
            var availability = await _cartService.CheckAvailabilityAsync(storeSkuId);
            return Ok(availability);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("skus/availability")]
    [ProducesResponseType(typeof(List<SkuAvailabilityDto>), 200)]
    public async Task<IActionResult> CheckAvailabilityBatch([FromQuery] string skuIds)
    {
        if (string.IsNullOrWhiteSpace(skuIds))
            return BadRequest(new { message = "skuIds query parameter is required." });

        var ids = skuIds.Split(',')
            .Select(s => int.TryParse(s.Trim(), out var id) ? id : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
            return BadRequest(new { message = "No valid SKU IDs provided." });

        var availability = await _cartService.CheckAvailabilityBatchAsync(ids);
        return Ok(availability);
    }

    [HttpPost("checkout")]
    [ProducesResponseType(typeof(StoreCheckoutResultDto), 200)]
    public async Task<IActionResult> Checkout([FromBody] StoreCheckoutRequest request)
    {
        var (jobId, userId) = await ResolveContext();
        try
        {
            var result = await _cartService.CheckoutAsync(jobId, userId, userId, request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("payment-methods")]
    [ProducesResponseType(typeof(List<PaymentMethodOptionDto>), 200)]
    public async Task<IActionResult> GetPaymentMethods(CancellationToken ct)
    {
        var methods = await _accountingRepo.GetPaymentMethodOptionsAsync(ct);
        return Ok(methods);
    }

    // ═══════════════════════════════════════════
    //  ANALYTICS & ADMIN
    // ═══════════════════════════════════════════

    [HttpGet("analytics/sales-pivot")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(List<StoreSalesPivotDto>), 200)]
    public async Task<IActionResult> GetSalesPivot()
    {
        var (jobId, _) = await ResolveContext();
        var data = await _adminService.GetSalesPivotAsync(jobId);
        return Ok(data);
    }

    [HttpGet("analytics/sales-by-item")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(List<StoreSalesByItemDto>), 200)]
    public async Task<IActionResult> GetSalesByItem()
    {
        var (jobId, _) = await ResolveContext();
        var data = await _adminService.GetSalesByItemAsync(jobId);
        return Ok(data);
    }

    [HttpGet("analytics/payments")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(List<StorePaymentDetailDto>), 200)]
    public async Task<IActionResult> GetPaymentDetails([FromQuery] bool walkUpOnly = false)
    {
        var (jobId, _) = await ResolveContext();
        var data = await _adminService.GetPaymentDetailsAsync(jobId, walkUpOnly);
        return Ok(data);
    }

    [HttpGet("analytics/family-purchases")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(List<StoreFamilyPurchaseDto>), 200)]
    public async Task<IActionResult> GetFamilyPurchases()
    {
        var (jobId, _) = await ResolveContext();
        var data = await _adminService.GetFamilyPurchasesAsync(jobId);
        return Ok(data);
    }

    [HttpGet("analytics/family-purchases/{familyUserId}")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(StoreFamilyPurchaseDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetFamilyPurchaseHistory(string familyUserId)
    {
        var (jobId, _) = await ResolveContext();
        var data = await _adminService.GetFamilyPurchaseHistoryAsync(jobId, familyUserId);
        if (data == null) return NotFound();
        return Ok(data);
    }

    [HttpGet("analytics/refunded")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(List<StoreRefundedItemDto>), 200)]
    public async Task<IActionResult> GetRefundedItems()
    {
        var (jobId, _) = await ResolveContext();
        var data = await _adminService.GetRefundedItemsAsync(jobId);
        return Ok(data);
    }

    [HttpGet("analytics/restocked")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(List<StoreRestockedItemDto>), 200)]
    public async Task<IActionResult> GetRestockedItems()
    {
        var (jobId, _) = await ResolveContext();
        var data = await _adminService.GetRestockedItemsAsync(jobId);
        return Ok(data);
    }

    [HttpPost("admin/restock")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> LogRestock([FromBody] LogRestockRequest request)
    {
        var (jobId, userId) = await ResolveContext();
        try
        {
            await _adminService.LogRestockAsync(jobId, userId, request);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("admin/sign-for-pickup")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> SignForPickup([FromBody] SignForPickupRequest request)
    {
        var (jobId, userId) = await ResolveContext();
        try
        {
            await _adminService.SignForPickupAsync(jobId, userId, request);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ═══════════════════════════════════════════
    //  WALK-UP — Anonymous Registration
    // ═══════════════════════════════════════════

    [HttpPost("walk-up-register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(StoreWalkUpRegisterResponse), 200)]
    public async Task<IActionResult> WalkUpRegister([FromBody] StoreWalkUpRegisterRequest request)
    {
        try
        {
            var result = await _walkUpService.RegisterAsync(request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ═══════════════════════════════════════════
    //  PRIVATE HELPERS
    // ═══════════════════════════════════════════

    private async Task<(Guid jobId, string userId)> ResolveContext()
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            throw new InvalidOperationException("Registration context required");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User context required");

        return (jobId.Value, userId);
    }
}
