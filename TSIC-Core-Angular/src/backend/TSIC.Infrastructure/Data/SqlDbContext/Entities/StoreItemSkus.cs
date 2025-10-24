using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class StoreItemSkus
{
    public int StoreSkuId { get; set; }

    public int StoreItemId { get; set; }

    public int? StoreColorId { get; set; }

    public int? StoreSizeId { get; set; }

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public bool Active { get; set; }

    public int MaxCanSell { get; set; }

    public virtual AspNetUsers LebUser { get; set; } = null!;

    public virtual ICollection<StoreCartBatchSkuQuantityAdjustments> StoreCartBatchSkuQuantityAdjustments { get; set; } = new List<StoreCartBatchSkuQuantityAdjustments>();

    public virtual ICollection<StoreCartBatchSkus> StoreCartBatchSkus { get; set; } = new List<StoreCartBatchSkus>();

    public virtual StoreColors? StoreColor { get; set; }

    public virtual StoreItems StoreItem { get; set; } = null!;

    public virtual StoreSizes? StoreSize { get; set; }
}
