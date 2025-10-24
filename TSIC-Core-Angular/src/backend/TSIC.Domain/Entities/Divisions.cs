using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class Divisions
{
    public Guid AgegroupId { get; set; }

    public Guid DivId { get; set; }

    public string? DivName { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public int? MaxRoundNumberToShow { get; set; }

    public virtual Agegroups Agegroup { get; set; } = null!;

    public virtual ICollection<BracketSeeds> BracketSeedsT1SeedDiv { get; set; } = new List<BracketSeeds>();

    public virtual ICollection<BracketSeeds> BracketSeedsT2SeedDiv { get; set; } = new List<BracketSeeds>();

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual ICollection<PushNotifications> PushNotifications { get; set; } = new List<PushNotifications>();

    public virtual ICollection<Schedule> ScheduleDiv { get; set; } = new List<Schedule>();

    public virtual ICollection<Schedule> ScheduleDiv2 { get; set; } = new List<Schedule>();

    public virtual ICollection<Teams> Teams { get; set; } = new List<Teams>();

    public virtual ICollection<TimeslotsLeagueSeasonDates> TimeslotsLeagueSeasonDates { get; set; } = new List<TimeslotsLeagueSeasonDates>();

    public virtual ICollection<TimeslotsLeagueSeasonFields> TimeslotsLeagueSeasonFields { get; set; } = new List<TimeslotsLeagueSeasonFields>();
}
