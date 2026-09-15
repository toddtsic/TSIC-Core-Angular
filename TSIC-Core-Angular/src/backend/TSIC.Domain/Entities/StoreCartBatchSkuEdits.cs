using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class StoreCartBatchSkuEdits
{
    public int StoreCartBatchSkuEditId { get; set; }

    public int StoreCartBatchSkuId { get; set; }

    public int PreviousStoreCartBatchSkuId { get; set; }

    public int PreviousStoreCartBatchSkuQuantity { get; set; }

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public virtual AspNetUsers LebUser { get; set; } = null!;

    public virtual StoreCartBatchSkus PreviousStoreCartBatchSku { get; set; } = null!;

    public virtual StoreCartBatchSkus StoreCartBatchSku { get; set; } = null!;
}
