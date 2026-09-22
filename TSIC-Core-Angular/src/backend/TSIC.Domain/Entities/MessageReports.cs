using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class MessageReports
{
    public Guid ReportId { get; set; }

    public Guid MessageId { get; set; }

    public string ReporterUserId { get; set; } = null!;

    public Guid ReporterRegId { get; set; }

    public byte Reason { get; set; }

    public string? Note { get; set; }

    public DateTime Created { get; set; }

    public byte Status { get; set; }

    public string? ResolvedByUserId { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual Messages Message { get; set; } = null!;

    public virtual Registrations ReporterReg { get; set; } = null!;

    public virtual AspNetUsers ReporterUser { get; set; } = null!;

    public virtual AspNetUsers? ResolvedByUser { get; set; }
}
