using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class TeamEvent
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

    public virtual Job? Job { get; set; }

    public virtual Team? Team { get; set; }

    public virtual AspNetUser User { get; set; } = null!;
}
