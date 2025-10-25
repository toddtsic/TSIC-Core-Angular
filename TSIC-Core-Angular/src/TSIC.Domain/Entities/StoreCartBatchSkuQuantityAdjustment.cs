using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class StoreCartBatchSkuQuantityAdjustment
{
    public int StoreCartBatchSkuQuantityAdjustmentsId { get; set; }

    public int StoreCartId { get; set; }

    public int StoreSkuId { get; set; }

    public int FromQuantity { get; set; }

    public int ToQuantity { get; set; }

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public virtual AspNetUser LebUser { get; set; } = null!;

    public virtual StoreCart StoreCart { get; set; } = null!;

    public virtual StoreItemSku StoreSku { get; set; } = null!;
}
