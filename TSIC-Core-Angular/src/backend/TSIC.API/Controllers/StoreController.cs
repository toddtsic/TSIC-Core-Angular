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
    private readonly IStoreReceiptService _receiptService;
    private readonly IRegistrationAccountingRepository _accountingRepo;
    private readonly IStoreCartRepository _cartRepo;
    private readonly IStoreImageService _imageService;
    private readonly IStoreSalesOpsService _salesOpsService;
    private readonly IStoreCampaignService _campaignService;
    private readonly IEmailBatchJobRegistry _batchJobs;

    public StoreController(
        IStoreCatalogService catalogService,
        IStoreCartService cartService,
        IStoreAdminService adminService,
        IJobLookupService jobLookupService,
        IStoreWalkUpService walkUpService,
        IStoreReceiptService receiptService,
        IRegistrationAccountingRepository accountingRepo,
        IStoreCartRepository cartRepo,
        IStoreImageService imageService,
        IStoreSalesOpsService salesOpsService,
        IStoreCampaignService campaignService,
        IEmailBatchJobRegistry batchJobs)
    {
        _catalogService = catalogService;
        _cartService = cartService;
        _adminService = adminService;
        _jobLookupService = jobLookupService;
        _walkUpService = walkUpService;
        _receiptService = receiptService;
        _accountingRepo = accountingRepo;
        _cartRepo = cartRepo;
        _imageService = imageService;
        _salesOpsService = salesOpsService;
        _campaignService = campaignService;
        _batchJobs = batchJobs;
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

    /// <summary>
    /// Legacy StoreSkusController.UpdateSku, action "remove" — delete one SKU.
    /// </summary>
    [HttpDelete("skus/{storeSkuId:int}")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteSku(int storeSkuId)
    {
        var (jobId, _) = await ResolveContext();
        try
        {
            await _catalogService.DeleteSkuAsync(jobId, storeSkuId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Legacy StoreSkusController.UpdateSku, action "batch" — delete every SKU of the item, then
    /// the item itself.
    /// </summary>
    [HttpDelete("items/{storeItemId:int}")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteItem(int storeItemId)
    {
        var (jobId, _) = await ResolveContext();
        try
        {
            await _catalogService.DeleteItemAsync(jobId, storeItemId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ═══════════════════════════════════════════
    //  SALES OPERATIONS — Admin
    //  Legacy StoreSales/Index + StoreSalesWalkup/Index and their row commands.
    // ═══════════════════════════════════════════

    /// <summary>
    /// Every purchased line in the store. <paramref name="walkUpOnly"/> is legacy's separate
    /// StoreSalesWalkup screen — same grid, counter sales only.
    /// </summary>
    [HttpGet("sales/lines")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(List<StoreSaleLineDto>), 200)]
    public async Task<IActionResult> GetSaleLines([FromQuery] bool walkUpOnly, CancellationToken ct)
    {
        var (jobId, _) = await ResolveContext();
        try
        {
            return Ok(await _salesOpsService.GetSaleLinesAsync(jobId, walkUpOnly, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Variants this line could be exchanged for — same item, active, in stock.</summary>
    [HttpGet("sales/lines/{storeCartBatchSkuId:int}/swap-options")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(List<StoreSwapOptionDto>), 200)]
    public async Task<IActionResult> GetSwapOptions(int storeCartBatchSkuId, CancellationToken ct)
    {
        var (jobId, _) = await ResolveContext();
        try
        {
            return Ok(await _salesOpsService.GetSwapOptionsAsync(jobId, storeCartBatchSkuId, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Whether the purchase's card charge has settled. The admin UI asks BEFORE opening the refund
    /// dialog, because an unsettled charge can only be voided in full.
    /// </summary>
    [HttpGet("sales/batches/{storeCartBatchId:int}/settled-status")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(StoreBatchSettledStatusDto), 200)]
    public async Task<IActionResult> GetBatchSettledStatus(int storeCartBatchId, CancellationToken ct)
    {
        var (jobId, _) = await ResolveContext();
        try
        {
            return Ok(await _salesOpsService.GetBatchSettledStatusAsync(jobId, storeCartBatchId, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Exchange units of a line for a different size or colour of the same item.</summary>
    [HttpPost("sales/swap")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> SwapCartSku(
        [FromBody] StoreSwapRequest request, CancellationToken ct)
    {
        var (jobId, userId) = await ResolveContext();
        try
        {
            await _salesOpsService.SwapCartSkuAsync(jobId, userId, request, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Refund a line or void the whole purchase. Returns 200 with Success=false for a gateway
    /// refusal — a declined refund is an answer the director needs to read, not a 500.
    /// </summary>
    [HttpPost("sales/refund")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(StoreRefundResponse), 200)]
    public async Task<IActionResult> RefundSale(
        [FromBody] StoreRefundRequest request, CancellationToken ct)
    {
        var (jobId, userId) = await ResolveContext();
        try
        {
            return Ok(await _salesOpsService.RefundAsync(jobId, userId, request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ═══════════════════════════════════════════
    //  EMAIL CAMPAIGNS — Admin
    //  Legacy StoreEmailAbandondedCarts / StoreEmailFamiliesThatNeverUsed /
    //  StoreEmailFamiliesThatOrdered — three near-identical controllers, one code path here.
    // ═══════════════════════════════════════════

    /// <summary>
    /// Opens a campaign: audience size, the seeded subject/body, the token palette, and — for
    /// <c>abandonedCarts</c> — the selectable cart grid and its age window.
    /// </summary>
    [HttpGet("campaigns/{kind}")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(StoreCampaignSetupDto), 200)]
    public async Task<IActionResult> GetCampaignSetup(
        StoreCampaignKind kind,
        [FromQuery] int? minAgeHours,
        [FromQuery] int? maxAgeHours,
        CancellationToken ct)
    {
        var (jobId, _) = await ResolveContext();
        try
        {
            return Ok(await _campaignService.GetSetupAsync(jobId, kind, minAgeHours, maxAgeHours, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Queues the campaign and returns immediately. Poll <c>campaigns/status/{batchJobId}</c> for
    /// progress; the sender receives a completion receipt when the batch drains.
    /// </summary>
    [HttpPost("campaigns/{kind}/send")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(StoreCampaignSendResponse), 200)]
    public async Task<IActionResult> SendCampaign(
        StoreCampaignKind kind,
        [FromBody] StoreCampaignSendRequest request,
        CancellationToken ct)
    {
        var (jobId, userId) = await ResolveContext();
        try
        {
            return Ok(await _campaignService.SendAsync(jobId, userId, kind, request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Progress + final summary for a queued campaign.</summary>
    [HttpGet("campaigns/status/{batchJobId:guid}")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(EmailBatchJobStatus), 200)]
    public ActionResult<EmailBatchJobStatus> GetCampaignStatus(Guid batchJobId)
    {
        var status = _batchJobs.Get(batchJobId);
        return status == null ? NotFound() : Ok(status);
    }

    // ═══════════════════════════════════════════
    //  IMAGES — Admin
    //  Legacy StoreImagesController. Files on the statics share are the source of truth;
    //  stores.StoreItemImage is a read index the service re-syncs on every call.
    // ═══════════════════════════════════════════

    /// <summary>
    /// Every image in the job's store, one row per file, with a placeholder row for each item
    /// that has no photo (legacy IStoreService.GetJobItemsPictures).
    /// </summary>
    [HttpGet("images")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(List<StoreItemImageDto>), 200)]
    public async Task<IActionResult> GetStoreImages(CancellationToken ct)
    {
        var (jobId, userId) = await ResolveContext();
        try
        {
            return Ok(await _imageService.GetStoreImagesAsync(jobId, userId, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("items/{storeItemId:int}/images")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(List<StoreItemImageDto>), 200)]
    public async Task<IActionResult> GetItemImages(int storeItemId, CancellationToken ct)
    {
        var (jobId, userId) = await ResolveContext();
        try
        {
            return Ok(await _imageService.GetItemImagesAsync(jobId, storeItemId, userId, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Add a photo. Capped at 10 per item, matching legacy MAX_IMAGES_PER_ITEM.
    /// </summary>
    [HttpPost("items/{storeItemId:int}/images")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(StoreItemImageDto), 200)]
    public async Task<IActionResult> AddItemImage(
        int storeItemId, IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file was uploaded." });

        var (jobId, userId) = await ResolveContext();
        try
        {
            await using var stream = file.OpenReadStream();
            var dto = await _imageService.AddItemImageAsync(
                jobId, storeItemId, stream, file.FileName, userId, ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Replace one photo in place, keeping its position in the item's image order.
    /// </summary>
    [HttpPut("items/{storeItemId:int}/images/{instance:int}")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(typeof(StoreItemImageDto), 200)]
    public async Task<IActionResult> ReplaceItemImage(
        int storeItemId, int instance, IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file was uploaded." });

        var (jobId, userId) = await ResolveContext();
        try
        {
            await using var stream = file.OpenReadStream();
            var dto = await _imageService.ReplaceItemImageAsync(
                jobId, storeItemId, instance, stream, file.FileName, userId, ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Delete a photo. Remaining photos are renumbered so instances stay contiguous from 1.
    /// </summary>
    [HttpDelete("items/{storeItemId:int}/images/{instance:int}")]
    [Authorize(Policy = "StoreAdmin")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteItemImage(
        int storeItemId, int instance, CancellationToken ct)
    {
        var (jobId, userId) = await ResolveContext();
        try
        {
            await _imageService.DeleteItemImageAsync(jobId, storeItemId, instance, userId, ct);
            return NoContent();
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

    [HttpGet("receipt/{storeCartBatchId:int}")]
    [ProducesResponseType(typeof(FileContentResult), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetReceipt(int storeCartBatchId, CancellationToken ct)
    {
        var (jobId, _) = await ResolveContext();
        var pdf = await _receiptService.GenerateReceiptPdfAsync(jobId, storeCartBatchId, ct);
        if (pdf == null) return NotFound();
        return File(pdf, "application/pdf", $"receipt-{storeCartBatchId}.pdf");
    }

    [HttpGet("family-players")]
    [ProducesResponseType(typeof(List<StoreFamilyPlayerDto>), 200)]
    public async Task<IActionResult> GetFamilyPlayers(CancellationToken ct)
    {
        var (jobId, userId) = await ResolveContext();
        var players = await _cartRepo.GetFamilyPlayersForJobAsync(userId, jobId, ct);
        return Ok(players);
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
