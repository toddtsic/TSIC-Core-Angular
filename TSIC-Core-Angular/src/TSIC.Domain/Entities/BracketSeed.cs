using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class BracketSeed
{
    public int AId { get; set; }

    public int Gid { get; set; }

    public int? WhichSide { get; set; }

    public Guid? T1SeedDivId { get; set; }

    public int? T1SeedRank { get; set; }

    public Guid? T2SeedDivId { get; set; }

    public int? T2SeedRank { get; set; }

    public string? LebUserId { get; set; }

    public DateTime? Modified { get; set; }

    public virtual Schedule GidNavigation { get; set; } = null!;

    public virtual AspNetUser? LebUser { get; set; }

    public virtual Division? T1SeedDiv { get; set; }

    public virtual Division? T2SeedDiv { get; set; }
}
