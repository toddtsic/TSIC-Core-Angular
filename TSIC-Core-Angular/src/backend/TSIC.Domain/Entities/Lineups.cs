using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Lineups
{
    public Guid EventId { get; set; }

    public Guid RegId { get; set; }

    public string? Position { get; set; }

    public bool IsStarter { get; set; }

    public int SortOrder { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual Events Event { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual Registrations Reg { get; set; } = null!;
}
