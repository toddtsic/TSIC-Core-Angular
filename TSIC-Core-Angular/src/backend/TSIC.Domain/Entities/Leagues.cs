using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Leagues
{
    public bool BAllowCoachScoreEntry { get; set; }

    public bool BHideContacts { get; set; }

    public bool BHideStandings { get; set; }

    public bool BShowScheduleToTeamMembers { get; set; }

    public bool BTakeAttendance { get; set; }

    public bool BTrackPenaltyMinutes { get; set; }

    public bool BTrackSportsmanshipScores { get; set; }

    public Guid LeagueId { get; set; }

    public string? LeagueName { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public string? RescheduleEmailsToAddon { get; set; }

    public Guid SportId { get; set; }

    public string? StrLop { get; set; }

    public string? StrGradYears { get; set; }

    public int? PointsMethod { get; set; }

    public int? StandingsSortProfileId { get; set; }

    public decimal? PlayerFeeOverride { get; set; }

    public virtual ICollection<Agegroups> Agegroups { get; set; } = new List<Agegroups>();

    public virtual ICollection<FieldOverridesStartTimeMaxMinGames> FieldOverridesStartTimeMaxMinGames { get; set; } = new List<FieldOverridesStartTimeMaxMinGames>();

    public virtual ICollection<FieldsLeagueSeason> FieldsLeagueSeason { get; set; } = new List<FieldsLeagueSeason>();

    public virtual ICollection<JobLeagues> JobLeagues { get; set; } = new List<JobLeagues>();

    public virtual ICollection<LeagueAgeGroupGameDayInfo> LeagueAgeGroupGameDayInfo { get; set; } = new List<LeagueAgeGroupGameDayInfo>();

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual ICollection<PushNotifications> PushNotifications { get; set; } = new List<PushNotifications>();

    public virtual ICollection<Schedule> Schedule { get; set; } = new List<Schedule>();

    public virtual Sports Sport { get; set; } = null!;

    public virtual StandingsSortProfiles? StandingsSortProfile { get; set; }

    public virtual ICollection<Teams> Teams { get; set; } = new List<Teams>();
}
