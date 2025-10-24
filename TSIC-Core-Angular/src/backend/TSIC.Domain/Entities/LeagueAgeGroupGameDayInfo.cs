using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class LeagueAgeGroupGameDayInfo
{
    public Guid? LeagueId { get; set; }

    public Guid? AgegroupId { get; set; }

    public DateTime? GDay { get; set; }

    public int? Stage { get; set; }

    public string? StartTime { get; set; }

    public int? GamestartInterval { get; set; }

    public int? MinGamesPerField { get; set; }

    public int? MaxGamesPerField { get; set; }

    public bool? BActive { get; set; }

    public string? Season { get; set; }

    public string? Year { get; set; }

    public int Ai { get; set; }

    public virtual Agegroups? Agegroup { get; set; }

    public virtual Leagues? League { get; set; }
}
