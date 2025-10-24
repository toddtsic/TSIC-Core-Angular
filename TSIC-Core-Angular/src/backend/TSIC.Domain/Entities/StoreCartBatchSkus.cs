using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class StoreCartBatchSkus
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

    public virtual Registrations? DirectToReg { get; set; }

    public virtual AspNetUsers LebUser { get; set; } = null!;

    public virtual StoreCartBatches StoreCartBatch { get; set; } = null!;

    public virtual ICollection<StoreCartBatchSkuEdits> StoreCartBatchSkuEditsPreviousStoreCartBatchSku { get; set; } = new List<StoreCartBatchSkuEdits>();

    public virtual ICollection<StoreCartBatchSkuEdits> StoreCartBatchSkuEditsStoreCartBatchSku { get; set; } = new List<StoreCartBatchSkuEdits>();

    public virtual ICollection<StoreCartBatchSkuRestocks> StoreCartBatchSkuRestocks { get; set; } = new List<StoreCartBatchSkuRestocks>();

    public virtual StoreItemSkus StoreSku { get; set; } = null!;
}
