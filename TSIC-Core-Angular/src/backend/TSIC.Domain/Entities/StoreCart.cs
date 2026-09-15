using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class StoreCart
{
    public int StoreCartId { get; set; }

    public int StoreId { get; set; }

    public string FamilyUserId { get; set; } = null!;

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public virtual AspNetUsers FamilyUser { get; set; } = null!;

    public virtual AspNetUsers LebUser { get; set; } = null!;

    public virtual Stores Store { get; set; } = null!;

    public virtual ICollection<StoreCartBatchSkuQuantityAdjustments> StoreCartBatchSkuQuantityAdjustments { get; set; } = new List<StoreCartBatchSkuQuantityAdjustments>();

    public virtual ICollection<StoreCartBatches> StoreCartBatches { get; set; } = new List<StoreCartBatches>();
}
