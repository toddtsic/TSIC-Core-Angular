using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobAdminChargeType
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public virtual ICollection<JobAdminCharge> JobAdminCharges { get; set; } = new List<JobAdminCharge>();
}
