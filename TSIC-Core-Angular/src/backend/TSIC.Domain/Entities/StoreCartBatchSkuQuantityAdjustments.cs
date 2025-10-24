using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class StoreCartBatchSkuQuantityAdjustments
{
    public int StoreCartBatchSkuQuantityAdjustmentsId { get; set; }

    public int StoreCartId { get; set; }

    public int StoreSkuId { get; set; }

    public int FromQuantity { get; set; }

    public int ToQuantity { get; set; }

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public virtual AspNetUsers LebUser { get; set; } = null!;

    public virtual StoreCart StoreCart { get; set; } = null!;

    public virtual StoreItemSkus StoreSku { get; set; } = null!;
}
