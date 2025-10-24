using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class BracketDataSingleElimination
{
    public int Id { get; set; }

    public string RoundType { get; set; } = null!;

    public int? T1 { get; set; }

    public int? T2 { get; set; }

    public int? SortOrder { get; set; }
}
