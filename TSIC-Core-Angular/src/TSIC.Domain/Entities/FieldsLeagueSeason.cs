using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class FieldsLeagueSeason
{
    public bool? BActive { get; set; }

    public Guid FieldId { get; set; }

    public Guid FlsId { get; set; }

    public Guid LeagueId { get; set; }

    public string? LebUserId { get; set; }

    public DateTime? Modified { get; set; }

    public string? Season { get; set; }

    public virtual Fields Field { get; set; } = null!;

    public virtual Leagues League { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }
}
