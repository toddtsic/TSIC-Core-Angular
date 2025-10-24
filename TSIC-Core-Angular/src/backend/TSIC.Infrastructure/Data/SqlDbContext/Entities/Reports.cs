using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class Reports
{
    public string ReportName { get; set; } = null!;

    public virtual ICollection<JobMenuItems> JobMenuItems { get; set; } = new List<JobMenuItems>();
}
