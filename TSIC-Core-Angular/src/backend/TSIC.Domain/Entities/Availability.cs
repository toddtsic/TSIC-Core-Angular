using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Availability
{
    public Guid EventId { get; set; }

    public Guid RegId { get; set; }

    public byte Response { get; set; }

    public string? Note { get; set; }

    public string AnsweredByUserId { get; set; } = null!;

    public DateTime AnsweredAt { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual AspNetUsers AnsweredByUser { get; set; } = null!;

    public virtual Events Event { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual Registrations Reg { get; set; } = null!;
}
