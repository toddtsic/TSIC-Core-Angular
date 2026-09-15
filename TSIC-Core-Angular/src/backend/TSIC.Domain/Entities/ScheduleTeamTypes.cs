using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class ScheduleTeamTypes
{
    public string TeamTypeId { get; set; } = null!;

    public string TeamTypeDesc { get; set; } = null!;

    public virtual ICollection<PairingsLeagueSeason> PairingsLeagueSeasonT1TypeNavigation { get; set; } = new List<PairingsLeagueSeason>();

    public virtual ICollection<PairingsLeagueSeason> PairingsLeagueSeasonT2TypeNavigation { get; set; } = new List<PairingsLeagueSeason>();

    public virtual ICollection<Schedule> ScheduleT1TypeNavigation { get; set; } = new List<Schedule>();

    public virtual ICollection<Schedule> ScheduleT2TypeNavigation { get; set; } = new List<Schedule>();
}
