using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class SeedAssignments
{
    public int SeedAssignmentId { get; set; }

    public int BracketInstanceId { get; set; }

    public int Gid { get; set; }

    public byte TargetSlot { get; set; }

    public Guid? SeedDivId { get; set; }

    public int SeedRank { get; set; }

    public int? AcrossPoolRank { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual BracketInstances BracketInstance { get; set; } = null!;

    public virtual Schedule GidNavigation { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual Divisions? SeedDiv { get; set; }
}
