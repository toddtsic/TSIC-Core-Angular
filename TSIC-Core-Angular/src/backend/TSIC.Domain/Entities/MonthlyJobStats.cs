using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class MonthlyJobStats
{
    public int Aid { get; set; }

    public Guid JobId { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public int? CountActivePlayersToDateLastMonth { get; set; }

    public int? CountActivePlayersToDate { get; set; }

    public int? CountNewPlayersThisMonth { get; set; }

    public int? CountActiveTeamsToDateLastMonth { get; set; }

    public int? CountActiveTeamsToDate { get; set; }

    public int? CountNewTeamsThisMonth { get; set; }

    public string LebUserId { get; set; } = null!;

    public DateTime Modified { get; set; }

    public virtual Jobs Job { get; set; } = null!;

    public virtual AspNetUsers LebUser { get; set; } = null!;
}
