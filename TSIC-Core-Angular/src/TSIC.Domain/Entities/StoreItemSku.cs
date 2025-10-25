using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class StoreItemSku
{
    public int StoreSkuId { get; set; }

    public int StoreItemId { get; set; }

    public int? StoreColorId { get; set; }

    public int? StoreSizeId { get; set; }

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public bool Active { get; set; }

    public int MaxCanSell { get; set; }

    public virtual AspNetUser LebUser { get; set; } = null!;

    public virtual ICollection<StoreCartBatchSkuQuantityAdjustment> StoreCartBatchSkuQuantityAdjustments { get; set; } = new List<StoreCartBatchSkuQuantityAdjustment>();

    public virtual ICollection<StoreCartBatchSku> StoreCartBatchSkus { get; set; } = new List<StoreCartBatchSku>();

    public virtual StoreColor? StoreColor { get; set; }

    public virtual StoreItem StoreItem { get; set; } = null!;

    public virtual StoreSize? StoreSize { get; set; }
}
