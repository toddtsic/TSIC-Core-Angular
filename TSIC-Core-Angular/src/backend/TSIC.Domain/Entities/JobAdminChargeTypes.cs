using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class JobAdminChargeTypes
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public virtual ICollection<JobAdminCharges> JobAdminCharges { get; set; } = new List<JobAdminCharges>();
}
