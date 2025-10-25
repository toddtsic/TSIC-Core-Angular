using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class StoreCartBatchSku
{
    public int StoreCartBatchSkuId { get; set; }

    public int StoreCartBatchId { get; set; }

    public int StoreSkuId { get; set; }

    public Guid? DirectToRegId { get; set; }

    public bool Active { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal FeeProduct { get; set; }

    public decimal FeeProcessing { get; set; }

    public decimal SalesTax { get; set; }

    public decimal FeeTotal { get; set; }

    public decimal PaidTotal { get; set; }

    public decimal RefundedTotal { get; set; }

    public DateTime CreateDate { get; set; }

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public int Restocked { get; set; }

    public virtual Registration? DirectToReg { get; set; }

    public virtual AspNetUser LebUser { get; set; } = null!;

    public virtual StoreCartBatch StoreCartBatch { get; set; } = null!;

    public virtual ICollection<StoreCartBatchSkuEdit> StoreCartBatchSkuEditPreviousStoreCartBatchSkus { get; set; } = new List<StoreCartBatchSkuEdit>();

    public virtual ICollection<StoreCartBatchSkuEdit> StoreCartBatchSkuEditStoreCartBatchSkus { get; set; } = new List<StoreCartBatchSkuEdit>();

    public virtual ICollection<StoreCartBatchSkuRestock> StoreCartBatchSkuRestocks { get; set; } = new List<StoreCartBatchSkuRestock>();

    public virtual StoreItemSku StoreSku { get; set; } = null!;
}
