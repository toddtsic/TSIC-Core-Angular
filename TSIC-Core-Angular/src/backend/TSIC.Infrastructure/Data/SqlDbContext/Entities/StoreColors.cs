using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class StoreColors
{
    public int StoreColorId { get; set; }

    public string StoreColorName { get; set; } = null!;

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public virtual AspNetUsers LebUser { get; set; } = null!;

    public virtual ICollection<StoreItemSkus> StoreItemSkus { get; set; } = new List<StoreItemSkus>();
}
