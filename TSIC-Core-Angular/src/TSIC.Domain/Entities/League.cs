using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class League
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

    public virtual ICollection<Agegroup> Agegroups { get; set; } = new List<Agegroup>();

    public virtual ICollection<FieldOverridesStartTimeMaxMinGame> FieldOverridesStartTimeMaxMinGames { get; set; } = new List<FieldOverridesStartTimeMaxMinGame>();

    public virtual ICollection<FieldsLeagueSeason> FieldsLeagueSeasons { get; set; } = new List<FieldsLeagueSeason>();

    public virtual ICollection<JobLeague> JobLeagues { get; set; } = new List<JobLeague>();

    public virtual ICollection<LeagueAgeGroupGameDayInfo> LeagueAgeGroupGameDayInfos { get; set; } = new List<LeagueAgeGroupGameDayInfo>();

    public virtual AspNetUser? LebUser { get; set; }

    public virtual ICollection<PushNotification> PushNotifications { get; set; } = new List<PushNotification>();

    public virtual ICollection<Schedule> Schedules { get; set; } = new List<Schedule>();

    public virtual Sport Sport { get; set; } = null!;

    public virtual StandingsSortProfile? StandingsSortProfile { get; set; }

    public virtual ICollection<Team> Teams { get; set; } = new List<Team>();
}
