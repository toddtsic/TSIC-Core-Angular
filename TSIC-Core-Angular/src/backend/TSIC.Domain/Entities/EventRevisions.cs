using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class EventRevisions
{
    public Guid RevisionId { get; set; }

    public Guid EventId { get; set; }

    public int Revision { get; set; }

    public string PriorJson { get; set; } = null!;

    public string ChangedByUserId { get; set; } = null!;

    public DateTime ChangedAt { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual AspNetUsers ChangedByUser { get; set; } = null!;

    public virtual Events Event { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }
}
