using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class StoreCartBatches
{
    public int StoreCartBatchId { get; set; }

    public int StoreCartId { get; set; }

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public DateTime? SignedForDate { get; set; }

    public string? SignedForBy { get; set; }

    public virtual AspNetUsers LebUser { get; set; } = null!;

    public virtual StoreCart StoreCart { get; set; } = null!;

    public virtual ICollection<StoreCartBatchAccounting> StoreCartBatchAccounting { get; set; } = new List<StoreCartBatchAccounting>();

    public virtual ICollection<StoreCartBatchSkus> StoreCartBatchSkus { get; set; } = new List<StoreCartBatchSkus>();
}
