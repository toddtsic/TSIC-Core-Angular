using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class TeamEvents
{
    public Guid EventId { get; set; }

    public Guid? TeamId { get; set; }

    public Guid? JobId { get; set; }

    public string Label { get; set; } = null!;

    public DateTime EventDate { get; set; }

    public DateTime CreateDate { get; set; }

    public string UserId { get; set; } = null!;

    public string? Comments { get; set; }

    public string? Location { get; set; }

    public int? MinutesDuration { get; set; }

    public string? Url { get; set; }

    public virtual Jobs? Job { get; set; }

    public virtual Teams? Team { get; set; }

    public virtual AspNetUsers User { get; set; } = null!;
}
