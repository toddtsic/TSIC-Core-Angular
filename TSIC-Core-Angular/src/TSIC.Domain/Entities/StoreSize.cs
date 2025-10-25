using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class StoreSize
{
    public int StoreSizeId { get; set; }

    public string StoreSizeName { get; set; } = null!;

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public virtual AspNetUser LebUser { get; set; } = null!;

    public virtual ICollection<StoreItemSku> StoreItemSkus { get; set; } = new List<StoreItemSku>();
}
