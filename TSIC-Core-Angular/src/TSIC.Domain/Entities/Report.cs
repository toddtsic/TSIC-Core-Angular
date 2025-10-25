using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Report
{
    public string ReportName { get; set; } = null!;

    public virtual ICollection<JobMenuItem> JobMenuItems { get; set; } = new List<JobMenuItem>();
}
