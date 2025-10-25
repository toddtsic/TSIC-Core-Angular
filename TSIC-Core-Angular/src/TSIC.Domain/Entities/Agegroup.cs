using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Agegroup
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

    public virtual ICollection<CalendarEvent> CalendarEvents { get; set; } = new List<CalendarEvent>();

    public virtual ICollection<Division> Divisions { get; set; } = new List<Division>();

    public virtual League League { get; set; } = null!;

    public virtual ICollection<LeagueAgeGroupGameDayInfo> LeagueAgeGroupGameDayInfos { get; set; } = new List<LeagueAgeGroupGameDayInfo>();

    public virtual AspNetUser? LebUser { get; set; }

    public virtual ICollection<PushNotification> PushNotifications { get; set; } = new List<PushNotification>();

    public virtual ICollection<Schedule> Schedules { get; set; } = new List<Schedule>();

    public virtual ICollection<Team> Teams { get; set; } = new List<Team>();

    public virtual ICollection<TimeslotsLeagueSeasonDate> TimeslotsLeagueSeasonDates { get; set; } = new List<TimeslotsLeagueSeasonDate>();

    public virtual ICollection<TimeslotsLeagueSeasonField> TimeslotsLeagueSeasonFields { get; set; } = new List<TimeslotsLeagueSeasonField>();
}
