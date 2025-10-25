using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class StoreItem
{
    public int StoreItemId { get; set; }

    public int StoreId { get; set; }

    public string StoreItemName { get; set; } = null!;

    public string? StoreItemComments { get; set; }

    public decimal StoreItemPrice { get; set; }

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public bool Active { get; set; }

    public int SortOrder { get; set; }

    public virtual AspNetUser LebUser { get; set; } = null!;

    public virtual Store Store { get; set; } = null!;

    public virtual ICollection<StoreItemSku> StoreItemSkus { get; set; } = new List<StoreItemSku>();
}
