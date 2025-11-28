using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Agegroups
{
    public Guid AgegroupId { get; set; }

    public string? AgegroupName { get; set; }

    public string? Color { get; set; }

    public string? Gender { get; set; }

    public Guid LeagueId { get; set; }

    public string? LebUserId { get; set; }

    public int MaxTeams { get; set; }

    public int MaxTeamsPerClub { get; set; }

    public DateTime Modified { get; set; }

    public decimal? RosterFee { get; set; }

    public string? RosterFeeLabel { get; set; }

    public string? Season { get; set; }

    public byte SortAge { get; set; }

    public decimal? TeamFee { get; set; }

    public string? TeamFeeLabel { get; set; }

    public DateOnly? DobMin { get; set; }

    public DateOnly? DobMax { get; set; }

    public short? SchoolGradeMin { get; set; }

    public short? SchoolGradeMax { get; set; }

    public int? GradYearMin { get; set; }

    public int? GradYearMax { get; set; }

    public decimal? LateFee { get; set; }

    public DateTime? LateFeeStart { get; set; }

    public DateTime? LateFeeEnd { get; set; }

    public bool? BAllowSelfRostering { get; set; }

    public decimal? DiscountFee { get; set; }

    public DateTime? DiscountFeeStart { get; set; }

    public DateTime? DiscountFeeEnd { get; set; }

    public bool? BChampionsByDivision { get; set; }

    public bool? BHideStandings { get; set; }

    public bool? BAllowApiRosterAccess { get; set; }

    public decimal? PlayerFeeOverride { get; set; }

    public virtual ICollection<CalendarEvents> CalendarEvents { get; set; } = new List<CalendarEvents>();

    public virtual ICollection<Divisions> Divisions { get; set; } = new List<Divisions>();

    public virtual Leagues League { get; set; } = null!;

    public virtual ICollection<LeagueAgeGroupGameDayInfo> LeagueAgeGroupGameDayInfo { get; set; } = new List<LeagueAgeGroupGameDayInfo>();

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual ICollection<PushNotifications> PushNotifications { get; set; } = new List<PushNotifications>();

    public virtual ICollection<Schedule> Schedule { get; set; } = new List<Schedule>();

    public virtual ICollection<Teams> Teams { get; set; } = new List<Teams>();

    public virtual ICollection<TimeslotsLeagueSeasonDates> TimeslotsLeagueSeasonDates { get; set; } = new List<TimeslotsLeagueSeasonDates>();

    public virtual ICollection<TimeslotsLeagueSeasonFields> TimeslotsLeagueSeasonFields { get; set; } = new List<TimeslotsLeagueSeasonFields>();
}
