using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class CalendarSync
{
    public Guid TeamId { get; set; }

    public string CalendarId { get; set; } = null!;

    public string? SyncToken { get; set; }

    public string? Etag { get; set; }

    public DateTime? LastAttemptAt { get; set; }

    public DateTime? LastSuccessAt { get; set; }

    public string? LastError { get; set; }

    public int ConsecutiveFailures { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual Teams Team { get; set; } = null!;
}
