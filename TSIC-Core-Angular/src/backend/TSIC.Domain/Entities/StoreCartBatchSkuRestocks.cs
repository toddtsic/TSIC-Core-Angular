using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class StoreCartBatchSkuRestocks
{
    public int StoreCartBatchSkuRestockId { get; set; }

    public int StoreCartBatchSkuId { get; set; }

    public int RestockCount { get; set; }

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public virtual AspNetUsers LebUser { get; set; } = null!;

    public virtual StoreCartBatchSkus StoreCartBatchSku { get; set; } = null!;
}
