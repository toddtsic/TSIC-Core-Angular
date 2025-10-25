using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Division
{
    public Guid AgegroupId { get; set; }

    public Guid DivId { get; set; }

    public string? DivName { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public int? MaxRoundNumberToShow { get; set; }

    public virtual Agegroup Agegroup { get; set; } = null!;

    public virtual ICollection<BracketSeed> BracketSeedT1SeedDivs { get; set; } = new List<BracketSeed>();

    public virtual ICollection<BracketSeed> BracketSeedT2SeedDivs { get; set; } = new List<BracketSeed>();

    public virtual AspNetUser? LebUser { get; set; }

    public virtual ICollection<PushNotification> PushNotifications { get; set; } = new List<PushNotification>();

    public virtual ICollection<Schedule> ScheduleDiv2s { get; set; } = new List<Schedule>();

    public virtual ICollection<Schedule> ScheduleDivs { get; set; } = new List<Schedule>();

    public virtual ICollection<Team> Teams { get; set; } = new List<Team>();

    public virtual ICollection<TimeslotsLeagueSeasonDate> TimeslotsLeagueSeasonDates { get; set; } = new List<TimeslotsLeagueSeasonDate>();

    public virtual ICollection<TimeslotsLeagueSeasonField> TimeslotsLeagueSeasonFields { get; set; } = new List<TimeslotsLeagueSeasonField>();
}
