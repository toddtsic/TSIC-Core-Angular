using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ReportExportType
{
    public string? ReportExportType1 { get; set; }

    public int ReportExportTypeId { get; set; }

    public virtual ICollection<JobMenuItem> JobMenuItems { get; set; } = new List<JobMenuItem>();
}
