using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ScheduleTeamType
{
    public string TeamTypeId { get; set; } = null!;

    public string TeamTypeDesc { get; set; } = null!;

    public virtual ICollection<PairingsLeagueSeason> PairingsLeagueSeasonT1TypeNavigations { get; set; } = new List<PairingsLeagueSeason>();

    public virtual ICollection<PairingsLeagueSeason> PairingsLeagueSeasonT2TypeNavigations { get; set; } = new List<PairingsLeagueSeason>();

    public virtual ICollection<Schedule> ScheduleT1TypeNavigations { get; set; } = new List<Schedule>();

    public virtual ICollection<Schedule> ScheduleT2TypeNavigations { get; set; } = new List<Schedule>();
}
