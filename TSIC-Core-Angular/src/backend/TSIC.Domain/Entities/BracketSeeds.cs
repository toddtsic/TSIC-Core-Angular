using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class BracketSeeds
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

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual Divisions? T1SeedDiv { get; set; }

    public virtual Divisions? T2SeedDiv { get; set; }
}
