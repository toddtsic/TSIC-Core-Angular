using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class BillingType
{
    public int BillingTypeId { get; set; }

    public string? BillingTypeName { get; set; }

    public string? JobTypeDesc { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();

    public virtual AspNetUser? LebUser { get; set; }
}
