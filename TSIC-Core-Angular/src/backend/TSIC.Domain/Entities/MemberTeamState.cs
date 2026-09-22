using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class MemberTeamState
{
    public Guid RegId { get; set; }

    public Guid TeamId { get; set; }

    public long LastReadSeq { get; set; }

    public bool Muted { get; set; }

    public DateTime? MutedUntil { get; set; }

    public TimeOnly? QuietStartLocal { get; set; }

    public TimeOnly? QuietEndLocal { get; set; }

    public bool NotifyOnMention { get; set; }

    public bool Pinned { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual Registrations Reg { get; set; } = null!;

    public virtual Teams Team { get; set; } = null!;
}
