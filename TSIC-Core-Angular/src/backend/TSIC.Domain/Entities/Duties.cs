using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Duties
{
    public Guid DutyId { get; set; }

    public Guid EventId { get; set; }

    public string Label { get; set; } = null!;

    public string? Notes { get; set; }

    public int SortOrder { get; set; }

    public Guid? AssignedRegId { get; set; }

    public string? AssignedByUserId { get; set; }

    public DateTime? AssignedAt { get; set; }

    public DateTime Created { get; set; }

    public string CreatorUserId { get; set; } = null!;

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual AspNetUsers? AssignedByUser { get; set; }

    public virtual Registrations? AssignedReg { get; set; }

    public virtual AspNetUsers CreatorUser { get; set; } = null!;

    public virtual Events Event { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }
}
