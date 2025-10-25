using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Store
{
    public int StoreId { get; set; }

    public Guid JobId { get; set; }

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public virtual Job Job { get; set; } = null!;

    public virtual AspNetUser LebUser { get; set; } = null!;

    public virtual ICollection<StoreCart> StoreCarts { get; set; } = new List<StoreCart>();

    public virtual ICollection<StoreItem> StoreItems { get; set; } = new List<StoreItem>();
}
