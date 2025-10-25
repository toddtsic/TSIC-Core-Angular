using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class StoreCartBatchSkuEdit
{
    public int StoreCartBatchSkuEditId { get; set; }

    public int StoreCartBatchSkuId { get; set; }

    public int PreviousStoreCartBatchSkuId { get; set; }

    public int PreviousStoreCartBatchSkuQuantity { get; set; }

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public virtual AspNetUser LebUser { get; set; } = null!;

    public virtual StoreCartBatchSku PreviousStoreCartBatchSku { get; set; } = null!;

    public virtual StoreCartBatchSku StoreCartBatchSku { get; set; } = null!;
}
