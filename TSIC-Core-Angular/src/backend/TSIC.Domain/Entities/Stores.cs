using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Stores
{
    public int StoreId { get; set; }

    public Guid JobId { get; set; }

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public virtual Jobs Job { get; set; } = null!;

    public virtual AspNetUsers LebUser { get; set; } = null!;

    public virtual ICollection<StoreCart> StoreCart { get; set; } = new List<StoreCart>();

    public virtual ICollection<StoreItems> StoreItems { get; set; } = new List<StoreItems>();
}
