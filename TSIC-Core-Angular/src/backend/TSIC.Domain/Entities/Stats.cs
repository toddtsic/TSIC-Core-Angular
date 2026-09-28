using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Stats
{
    public Guid EventId { get; set; }

    public Guid RegId { get; set; }

    public string StatKey { get; set; } = null!;

    public decimal Value { get; set; }

    public string RecordedByUserId { get; set; } = null!;

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual Events Event { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual AspNetUsers RecordedByUser { get; set; } = null!;

    public virtual Registrations Reg { get; set; } = null!;
}
