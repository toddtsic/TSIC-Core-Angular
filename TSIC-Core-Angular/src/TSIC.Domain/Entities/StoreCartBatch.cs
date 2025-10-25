using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class StoreCartBatch
{
    public int StoreCartBatchId { get; set; }

    public int StoreCartId { get; set; }

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public DateTime? SignedForDate { get; set; }

    public string? SignedForBy { get; set; }

    public virtual AspNetUser LebUser { get; set; } = null!;

    public virtual StoreCart StoreCart { get; set; } = null!;

    public virtual ICollection<StoreCartBatchAccounting> StoreCartBatchAccountings { get; set; } = new List<StoreCartBatchAccounting>();

    public virtual ICollection<StoreCartBatchSku> StoreCartBatchSkus { get; set; } = new List<StoreCartBatchSku>();
}
