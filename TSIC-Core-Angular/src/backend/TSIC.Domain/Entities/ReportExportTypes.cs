using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class ReportExportTypes
{
    public string? ReportExportType { get; set; }

    public int ReportExportTypeId { get; set; }

    public virtual ICollection<JobMenuItems> JobMenuItems { get; set; } = new List<JobMenuItems>();
}
