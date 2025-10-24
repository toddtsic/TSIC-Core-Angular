using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class JobReportExportHistory
{
    public int Id { get; set; }

    public Guid RegistrationId { get; set; }

    public string? StoredProcedureName { get; set; }

    public string? ReportName { get; set; }

    public DateTime? ExportDate { get; set; }

    public virtual Registrations Registration { get; set; } = null!;
}
