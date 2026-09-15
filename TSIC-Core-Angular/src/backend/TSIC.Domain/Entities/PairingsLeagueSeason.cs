using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class PairingsLeagueSeason
{
    public int Ai { get; set; }

    public int? GCnt { get; set; }

    public int? TCnt { get; set; }

    public int T1 { get; set; }

    public int T2 { get; set; }

    public string T1Type { get; set; } = null!;

    public string T2Type { get; set; } = null!;

    public int Rnd { get; set; }

    public Guid LeagueId { get; set; }

    public string Season { get; set; } = null!;

    public int GameNumber { get; set; }

    public int? T1GnoRef { get; set; }

    public int? T2GnoRef { get; set; }

    public string LebUserId { get; set; } = null!;

    public DateTime Modified { get; set; }

    public string? T1Annotation { get; set; }

    public string? T2Annotation { get; set; }

    public string? T1CalcType { get; set; }

    public string? T2CalcType { get; set; }

    public virtual AspNetUsers LebUser { get; set; } = null!;

    public virtual ScheduleTeamTypes T1TypeNavigation { get; set; } = null!;

    public virtual ScheduleTeamTypes T2TypeNavigation { get; set; } = null!;
}
