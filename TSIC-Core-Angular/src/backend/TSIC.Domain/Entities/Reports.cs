using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Reports
{
    public string ReportName { get; set; } = null!;

    public virtual ICollection<JobMenuItems> JobMenuItems { get; set; } = new List<JobMenuItems>();
}
