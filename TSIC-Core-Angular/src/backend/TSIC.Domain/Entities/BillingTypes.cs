using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class BillingTypes
{
    public int BillingTypeId { get; set; }

    public string? BillingTypeName { get; set; }

    public string? JobTypeDesc { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public virtual ICollection<Jobs> Jobs { get; set; } = new List<Jobs>();

    public virtual AspNetUsers? LebUser { get; set; }
}
